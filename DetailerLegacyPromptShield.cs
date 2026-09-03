namespace Spoomples.Extensions.WildcardImporter;

using System;
using System.Text;
using FreneticUtilities.FreneticExtensions;
using SwarmUI.Text2Image;

/// <summary>
/// Keeps SwarmUI's A1111/Comfy legacy prompt parser out of the <see cref="Detailer.DIRECTIVE"/> mask
/// expression.
///
/// <para>Core added <c>LegacyPromptParser.Convert</c> as the last act of <c>ProcessPromptLike</c>: it runs
/// after all three tag passes and rewrites <c>(x)</c> to <c>&lt;weight[1.1]:x&gt;</c>, <c>[a:0.5]</c> to
/// <c>&lt;fromto[0.5]:,a&gt;</c> and <c>[a|b]</c> to <c>&lt;alternate:a,b&gt;</c>. It recurses into the data
/// half of every <c>&lt;tag:data&gt;</c> with no allowlist and no per-tag opt-out, and the mask grammar spends
/// all four of those characters: <c>(</c><c>)</c> group and call arguments, <c>[</c><c>]</c> the feature index
/// and the leading <c>[blur:N,creativity:X]</c> block. Left alone, <c>&lt;wcdetailer:[blur:10](a|b)&gt;</c>
/// reaches the workflow generator as <c>&lt;wcdetailer:&lt;fromto[10]:,blur&gt;&lt;weight[1.1]:a|b&gt;&gt;</c>
/// and every parse of it fails.</para>
///
/// <para>The parser consumes one level of backslash escaping on exactly these characters, so escaping them
/// on the way out of the tag pass and letting the parser strip the backslashes restores the expression
/// byte-for-byte, which is why the directive's documented syntax does not have to change.</para>
/// </summary>
public static class DetailerLegacyPromptShield
{
    /// <summary>Grammar characters that are also legacy prompt syntax, and that the legacy parser will
    /// unescape for us. Escaping anything outside this set would survive into the mask expression.</summary>
    private static bool IsShielded(char c) => c is '(' or ')' or '[' or ']';

    /// <summary>
    /// Post-processor for the detailer directive: re-emits the tag with its mask expression escaped.
    ///
    /// <para>This has to be a post-processor rather than a basic processor, because escaping is only correct
    /// when exactly one <c>Convert</c> follows it, and <c>Convert</c> runs once per <c>ProcessPromptLike</c>
    /// level. A directive produced by a wildcard expansion has already been through the nested level's
    /// <c>Convert</c> (which stripped any escaping that level applied) by the time the outer level sees it -
    /// but every level runs the post-processor pass immediately before its own <c>Convert</c>, so the
    /// one-escape-one-unescape pairing holds at any nesting depth.</para>
    /// </summary>
    public static string ShieldFromLegacyParser(string data, T2IPromptHandling.PromptTagContext context)
    {
        string tag = context.RawCurrentTag;
        int colon = T2IPromptHandling.IndexOfNoncontained(tag, ':');
        if (colon == -1 || data.IndexOfAny(['(', ')', '[', ']']) == -1)
        {
            // Nothing to shield. Returning null hands the tag back to core's own fallthrough, which restores
            // the section id from the '//cid=' suffix and re-emits the tag unchanged.
            return null;
        }
        // Taking over the return means taking over that section id restore as well, or every later tag in
        // this pass is attributed to whichever section was current before this one.
        (_, string cidText) = tag.BeforeAndAfterLast("//cid=");
        if (!string.IsNullOrWhiteSpace(cidText) && int.TryParse(cidText, out int cid))
        {
            context.SectionID = cid;
        }
        // Rebuilt from the raw tag rather than from 'data' so the prefix, its casing and any predata survive
        // exactly. The '//cid=' suffix sits inside the escaped half and is unaffected by escaping.
        return $"<{tag[..colon]}:{Escape(tag[(colon + 1)..])}>";
    }

    /// <summary>Backslash-escapes the grammar characters the legacy parser would otherwise rewrite.</summary>
    public static string Escape(string val)
    {
        StringBuilder result = new(val.Length + 8);
        foreach (char c in val)
        {
            if (IsShielded(c))
            {
                result.Append('\\');
            }
            result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>
    /// Drops the backslashes <see cref="Escape"/> added.
    ///
    /// <para>Normally the legacy parser has already done this by the time the mask expression is read back
    /// out of the processed prompt, and this is a no-op. It is not a no-op when the user has turned off the
    /// "Parse Alternative Prompt Syntaxes" setting, because then <c>Convert</c> never runs and the
    /// backslashes are still there. Doing it unconditionally at the consuming end is what makes the shield
    /// independent of that setting.</para>
    /// </summary>
    public static string Unescape(string val)
    {
        if (val is null || !val.Contains('\\'))
        {
            return val;
        }
        StringBuilder result = new(val.Length);
        for (int i = 0; i < val.Length; i++)
        {
            if (val[i] == '\\' && i + 1 < val.Length && IsShielded(val[i + 1]))
            {
                i++;
            }
            result.Append(val[i]);
        }
        return result.ToString();
    }
}
