namespace Spoomples.Extensions.WildcardImporter
{
    using System.Collections;
    using SwarmUI.Text2Image;

    public class PromptTagContextDictionary : IDictionary<String, Object>
    {
        private readonly T2IPromptHandling.PromptTagContext _context;
        private readonly Dictionary<string, object> _extra = new();
        private static readonly HashSet<string> _forbiddenSymbols = new() { "any", "contains", "icontains", "length" };

        /// <summary>
        /// DIAGNOSTIC ONLY, off unless something assigns it. Invoked as (key, expression) every
        /// time a name resolves to the empty-string fallback below instead of to a real macro or
        /// variable. Python PPP treats that same read as a hard "Unknown user variable" error, so
        /// this is the hook for measuring how big that population is here before deciding whether
        /// the fallback should warn. Setting it changes no behaviour.
        /// </summary>
        public static Action<string, string> OnMissingKey;

        /// <summary>
        /// EXPERIMENTAL, off by default. When set, a name that resolves to the empty-string
        /// fallback raises a parser warning, matching what Python PPP reports as
        /// `Unknown user variable X`. Reads made by a `${x?=default}` guard are exempt -- see
        /// <see cref="IsDefaultGuard"/>.
        /// </summary>
        public static bool WarnOnMissingKey;

        /// <summary>
        /// Is this read the `${x?=default}` idiom rather than a mistake?
        ///
        /// That guard means "if this name holds nothing, give it one", and the transform emits it
        /// as <c>&lt;wcmatch:&lt;wccase[length(x) eq 0]:&lt;setmacro[x,false]:default&gt;&gt;&gt;</c>.
        /// Reading an unset name is the entire point, so it can never be the error PPP reports --
        /// and studio69 leans on it heavily (measured: every miss in 300 full renders was one of
        /// these, across just 5 names).
        ///
        /// Matching on the expression shape is deliberately conservative. Its failure mode is a
        /// MISSED warning (an author writing `length(x) eq 0` as a genuine condition), never a
        /// spurious one, which is the right way round for a check that would otherwise be noise.
        /// </summary>
        private static bool IsDefaultGuard(string key, string expression)
        {
            if (string.IsNullOrEmpty(expression)) { return false; }
            return System.Text.RegularExpressions.Regex.IsMatch(
                expression,
                $@"length\s*\(\s*{System.Text.RegularExpressions.Regex.Escape(key)}\s*\)\s*(eq|==)\s*0");
        }

        /// <summary>The condition currently being evaluated, for OnMissingKey's benefit. Set by
        /// PromptDirectives around each Mages compile/invoke; thread-static because conditions can
        /// be evaluated concurrently.</summary>
        [ThreadStatic]
        public static string CurrentExpression;

        public PromptTagContextDictionary(T2IPromptHandling.PromptTagContext context)
        {
            _context = context;
        }

        ICollection<string> IDictionary<string, object>.Keys => throw new NotImplementedException();

        ICollection<object> IDictionary<string, object>.Values => throw new NotImplementedException();

        int ICollection<KeyValuePair<string, object>>.Count => throw new NotImplementedException();

        bool ICollection<KeyValuePair<string, object>>.IsReadOnly => throw new NotImplementedException();

        object IDictionary<string, object>.this[string key]
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        void IDictionary<string, object>.Add(string key, object value)
        {
            _extra.Add(key, value);
        }

        bool IDictionary<string, object>.ContainsKey(string key)
        {
            // return _context.Macros.ContainsKey(key) || _context.Variables.ContainsKey(key) || _extra.ContainsKey(key);
            // we will return empty string instead of null for missing variables so from Mages perspective the key always exists
            return !_forbiddenSymbols.Contains(key);
        }

        bool IDictionary<string, object>.Remove(string key)
        {
            return _extra.Remove(key);
        }

        bool IDictionary<string, object>.TryGetValue(string key, out object value)
        {
            if (_context.Macros.TryGetValue(key, out var macro))
            {
                value = _context.Parse(macro);
                return true;
            }
            if (_context.Variables.TryGetValue(key, out var variable))
            {
                value = variable;
                return true;
            }

            if (_extra.TryGetValue(key, out value))
            {
                return true;
            }

            if (!_forbiddenSymbols.Contains(key))
            {
                OnMissingKey?.Invoke(key, CurrentExpression);
                if (WarnOnMissingKey && !IsDefaultGuard(key, CurrentExpression))
                {
                    _context.TrackWarning(
                        $"Unknown user variable '{key}' read in condition '{CurrentExpression}' - "
                        + "it has no macro or variable and evaluates as empty.");
                }
                // return empty string
                value = "";
                return true;
            }
            return false;
        }

        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item)
        {
            _extra.Add(item.Key, item.Value);
        }

        void ICollection<KeyValuePair<string, object>>.Clear()
        {
            _extra.Clear();
        }

        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item)
        {
            return _extra.Remove(item.Key);
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}


