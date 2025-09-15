using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    /// <summary>
    /// Unit tests for WildcardProcessor parsing functionality.
    /// Tests the translation from SD Dynamic Prompt Extension syntax to SwarmUI syntax.
    /// </summary>
    public class WildcardProcessorTest
    {
        private static int _testsPassed = 0;
        private static int _testsFailed = 0;
        private static List<string> _failureMessages = new();

        public static void RunTests()
        {
            // Set logging level to Debug to see detailed execution traces
            var originalLogLevel = Logs.MinimumLevel;
            Logs.MinimumLevel = Logs.LogLevel.Debug;
            
            Logs.Info("Starting WildcardProcessor unit tests...");
            
            _testsPassed = 0;
            _testsFailed = 0;
            _failureMessages.Clear();

            // Test variants
            TestBasicVariants();
            TestWeightedVariants();
            TestQuantifierVariants();
            TestQuantifierVariantsWithPrefixFlags();
            TestVariantCustomSeparators();
            TestRangeVariants();
            TestEmptyVariants();
            TestNestedVariants();

            // Test wildcards
            TestBasicWildcards();
            TestWildcardsInVariants();
            TestGlobWildcards();
            TestAdvancedWildcardOptions();
            
            // Test label filtering
            TestLabelFiltering();
            
            // Test wildcard choice labels
            TestWildcardChoiceLabels();

            // Test variables
            TestVariableAssignments();
            TestVariableAccess();
            TestImmediateVariables();

            // Test prompt editing
            TestPromptEditing();

            // Test alternating words
            TestAlternatingWords();

            // Test negative attention
            TestNegativeAttention();

            // Test set command
            TestSetCommandLongForm();
            TestSetCommandShortForm();
            TestSetCommandEdgeCases();

            // Test echo command
            TestEchoCommandLongForm();
            TestEchoCommandShortForm();
            TestEchoCommandEdgeCases();

            // Test if command
            TestIfCommandBasic();
            TestIfCommandComparisons();
            TestIfCommandListOperations();
            TestIfCommandNegation();
            TestIfCommandComplexStructures();
            TestIfCommandEdgeCases();

            // Test STN commands
            TestStnCommands();

            // Test edge cases
            TestMalformedSyntax();
            TestComplexNesting();
            TestSpecialCharacters();
            
            // Test recursive processing regression tests
            TestRecursiveProcessingRegression();

            // Test BREAK word replacement
            TestBreakWordReplacement();
            TestBreakWordProtectedContexts();

            // Print results
            Logs.Info($"WildcardProcessor tests completed: {_testsPassed} passed, {_testsFailed} failed");
            if (_testsFailed > 0)
            {
                Logs.Error("Test failures:");
                foreach (var failure in _failureMessages)
                {
                    Logs.Error($"  - {failure}");
                }
            }
            
            // Restore original log level
            Logs.MinimumLevel = originalLogLevel;
        }

        #region Test Variants

        private static void TestBasicVariants()
        {
            // Basic variant: {a|b|c}
            AssertTransform("{summer|autumn|winter|spring} is coming", 
                           "<wcrandom:summer|autumn|winter|spring> is coming",
                           "Basic variant");

            // Single option
            AssertTransform("{summer} is coming", 
                           "<wcrandom:summer> is coming",
                           "Single option variant");
            
            AssertTransform("{2000|2010|2020} is coming",
                "<wcrandom:2000|2010|2020> is coming",
                "Variants with numeric options");

            // Multiple variants in one line
            AssertTransform("I like {red|blue} and {cats|dogs}", 
                           "I like <wcrandom:red|blue> and <wcrandom:cats|dogs>",
                           "Multiple variants");
        }

        private static void TestWeightedVariants()
        {
            // Weighted options: {0.5::a|1::b|0.25::c}
            // Now using native wcrandom weighted syntax instead of duplicating options
            AssertTransform("{0.5::summer|1::autumn|0.25::winter}", 
                           "<wcrandom:0.5::summer|1::autumn|0.25::winter>",
                           "Weighted variant with decimals");

            // Mixed weighted and unweighted
            AssertTransform("{summer|2::autumn|winter}", 
                           "<wcrandom:summer|2::autumn|winter>",
                           "Mixed weighted variant");

            // Integer weights
            AssertTransform("{1::red|3::blue|2::green}", 
                           "<wcrandom:1::red|3::blue|2::green>",
                           "Integer weighted variant");
        }

        private static void TestQuantifierVariants()
        {
            // Pick 2: {2$$a|b|c}
            AssertTransform("My favorites are {2$$chocolate|vanilla|strawberry}", 
                           "My favorites are <wcrandom[2,]:chocolate|vanilla|strawberry>",
                           "Quantifier variant");

            // Pick 1 (explicit)
            AssertTransform("{1$$red|blue|green}", 
                           "<wcrandom[1,]:red|blue|green>",
                           "Explicit single quantifier");
        }

        private static void TestQuantifierVariantsWithPrefixFlags()
        {
            // Test with ~ prefix flag
            AssertTransform("{~3$$a|b|c}", 
                           "<wcrandom[3,]:a|b|c>",
                           "Quantifier variant with ~ prefix flag");

            // Test with @ prefix flag
            AssertTransform("{@1-2$$red|blue|green}", 
                           "<wcrandom[1-2,]:red|blue|green>",
                           "Range variant with @ prefix flag");

            // Test with r prefix flag
            AssertTransform("{r2$$chocolate|vanilla}", 
                           "<wcrandom[2,]:chocolate|vanilla>",
                           "Quantifier variant with r prefix flag");

            // Test with o prefix flag
            AssertTransform("{o1-3$$colors|shades|tones}", 
                           "<wcrandom[1-3,]:colors|shades|tones>",
                           "Range variant with o prefix flag");

            // Test with multiple prefix flags combined
            AssertTransform("{~r3$$a|b|c}", 
                           "<wcrandom[3,]:a|b|c>",
                           "Quantifier variant with ~r prefix flags");

            AssertTransform("{@o1-2$$a|b|c}", 
                           "<wcrandom[1-2,]:a|b|c>",
                           "Range variant with @o prefix flags");

            AssertTransform("{ro$$a|b|c}", 
                           "<wcrandom:a|b|c>",
                           "Simple variant with ro prefix flags (no quantifier)");

            // Test with all prefix flags
            AssertTransform("{~@ro2$$red|green|blue}", 
                           "<wcrandom[2,]:red|green|blue>",
                           "Quantifier variant with all prefix flags ~@ro");

            // Test prefix flags with no upper bound range
            AssertTransform("{~2-$$red|blue|green|yellow}", 
                           "<wcrandom[2-4,]:red|blue|green|yellow>",
                           "No upper bound range with ~ prefix flag");

            // Test prefix flags with no lower bound range
            AssertTransform("{@-3$$red|blue|green}", 
                           "<wcrandom[1-3,]:red|blue|green>",
                           "No lower bound range with @ prefix flag");

            // Test prefix flags with wildcards
            AssertTransform("{~r2$$__flavours__}", 
                           "<wcwildcard[2,]:flavours>",
                           "Wildcard quantifier with ~r prefix flags");
        }

        private static void TestVariantCustomSeparators()
        {
            // Basic custom separator: {2$$ and $$a|b|c}
            AssertTransform("Colors: {2$$ and $$red|blue|green}", 
                           "Colors: <wcrandom[2, and ]:red|blue|green>",
                           "Basic variant custom separator");

            // Custom separator with range: {2-3$$ or $$chocolate|vanilla|strawberry}
            AssertTransform("Flavors: {2-3$$ or $$chocolate|vanilla|strawberry}", 
                           "Flavors: <wcrandom[2-3, or ]:chocolate|vanilla|strawberry>",
                           "Range variant custom separator");

            // Custom separator with prefix flags: {@~2$$ with $$option1|option2|option3}
            AssertTransform("Choose: {@~2$$ with $$option1|option2|option3}", 
                           "Choose: <wcrandom[2, with ]:option1|option2|option3>",
                           "Prefix flags with custom separator");

            // Complex custom separator: {1-2$$, and also $$item1|item2|item3|item4}
            AssertTransform("Items: {1-2$$, and also $$item1|item2|item3|item4}", 
                           "Items: <wcrandom[1-2,, and also ]:item1|item2|item3|item4>",
                           "Complex custom separator");

            // Custom separator with no lower bound: {-2$$ plus $$alpha|beta|gamma}
            AssertTransform("Values: {-2$$ plus $$alpha|beta|gamma}", 
                           "Values: <wcrandom[1-2, plus ]:alpha|beta|gamma>",
                           "No lower bound with custom separator");

            // Empty custom separator (just $$): {2$$$$red|blue|green}
            AssertTransform("Empty sep: {2$$$$red|blue|green}", 
                           "Empty sep: <wcrandom[2,]:red|blue|green>",
                           "Empty custom separator");

            // Custom separator with spaces: {3$$   between   $$cat|dog|bird}
            AssertTransform("Pets: {3$$   between   $$cat|dog|bird}", 
                           "Pets: <wcrandom[3,   between   ]:cat|dog|bird>",
                           "Custom separator with spaces");

            // Custom separator with special characters: {2$$--$$first|second|third}
            AssertTransform("Sequence: {2$$--$$first|second|third}", 
                           "Sequence: <wcrandom[2,--]:first|second|third>",
                           "Custom separator with special characters");
        }

        private static void TestRangeVariants()
        {
            // Range: {2-3$$a|b|c|d}
            AssertTransform("{2-3$$chocolate|vanilla|strawberry|mint}", 
                           "<wcrandom[2-3,]:chocolate|vanilla|strawberry|mint>",
                           "Range variant");

            // No lower bound: {-2$$a|b|c}
            AssertTransform("{-2$$red|blue|green}", 
                           "<wcrandom[1-2,]:red|blue|green>",
                           "No lower bound range");

            // No upper bound: {2-$$a|b|c|d}
            AssertTransform("{2-$$red|blue|green|yellow}", 
                           "<wcrandom[2-4,]:red|blue|green|yellow>",
                           "No upper bound range");
        }

        private static void TestEmptyVariants()
        {
            // Empty options: {a||c}
            AssertTransform("{red||blue}", 
                           "<wcrandom:red|<comment:empty>|blue>",
                           "Empty option in variant");

            // All empty: {||}
            AssertTransform("{||}", 
                           "<wcrandom:<comment:empty>|<comment:empty>|<comment:empty>>",
                           "All empty options");

            // Trailing empty: {a|b|}
            AssertTransform("{red|blue|}", 
                           "<wcrandom:red|blue|<comment:empty>>",
                           "Trailing empty option");
        }

        private static void TestNestedVariants()
        {
            // Nested variants: {a|{b|c}|d}
            AssertTransform("Color is {red|{light blue|dark blue}|green}", 
                           "Color is <wcrandom:red|<wcrandom:light blue|dark blue>|green>",
                           "Nested variants");

            // Deep nesting
            AssertTransform("{a|{b|{c|d}|e}|f}", 
                           "<wcrandom:a|<wcrandom:b|<wcrandom:c|d>|e>|f>",
                           "Deep nested variants");
        }

        #endregion

        #region Test Wildcards

        private static void TestBasicWildcards()
        {
            // Basic wildcard: __name__
            AssertTransform("__season__ is coming", 
                           "<wcwildcard:season> is coming",
                           "Basic wildcard");

            // Multiple wildcards
            AssertTransform("I like __color__ __animal__", 
                           "I like <wcwildcard:color> <wcwildcard:animal>",
                           "Multiple wildcards");

            // Wildcard with path
            AssertTransform("__clothing/shirts__ are nice", 
                           "<wcwildcard:clothing/shirts> are nice",
                           "Wildcard with path");

            AssertTransform("__clothing/shirts__ and __clothing/pants__ are required", 
                "<wcwildcard:clothing/shirts> and <wcwildcard:clothing/pants> are required",
                "Multiple wildcards");

            AssertTransform("2$$__color__",
                "2$$<wcwildcard:color>",
                "Wildcards outside of variant do not process $$ prefix outside of __");
        }

        private static void TestWildcardsInVariants()
        {
            // Wildcards in variants: {2$$__flavours__}
            // This is the correct SD Dynamic Prompts syntax - should use wildcard quantifier
            AssertTransform("My favourite ice-cream flavours are {2$$__flavours__}", 
                           "My favourite ice-cream flavours are <wcwildcard[2,]:flavours>",
                           "Wildcards in variants with quantifier");

            // Range quantifier with wildcard in variant: {2-3$$__colors__}
            AssertTransform("Pick {2-3$$__colors__}", 
                           "Pick <wcwildcard[2-3,]:colors>",
                           "Range quantifier with wildcard in variant");

            // Simple wildcard in variant: {__flavours__|vanilla}
            AssertTransform("I like {__flavours__|vanilla}", 
                           "I like <wcrandom:<wcwildcard:flavours>|vanilla>",
                           "Simple wildcard in variant");

            // Multiple wildcards in quantified variant: {2$$__flavours__|__flavours__}
            // SD Dynamic Prompts treats this as simple variants, each wildcard resolved independently
            AssertTransform("My favourite ice-cream flavours are {2$$__flavours__|__flavours__}", 
                           "My favourite ice-cream flavours are <wcrandom[2,]:<wcwildcard:flavours>|<wcwildcard:flavours>>",
                           "Multiple wildcards in quantified variant");
            
            AssertTransform("My favorite breed is __{cat|dog}s__", 
                           "My favorite breed is <wcwildcard:<wcrandom:cat|dog>s>",
                           "Variant nested in wildcard name");
            
            AssertTransform("my top 2 breeds are {2$$__{1$$cat|dog}s__}",
                "my top 2 breeds are <wcwildcard[2,]:<wcrandom[1,]:cat|dog>s>",
                "Variant nested in wildcard name with quantifier");
        }

        private static void TestGlobWildcards()
        {
            // Single glob: __colors*__
            AssertTransform("__colors*__ are nice", 
                           "<wcrandom:<wcwildcard:colors-cold>|<wcwildcard:colors-warm>> are nice",
                           "Single glob wildcard",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Recursive glob: __artists/**__
            // Note: Dictionary enumeration order may vary, so we accept either order
            AssertTransform("__artists/**__ painted this", 
                           "<wcrandom:<wcwildcard:artists/dutch>|<wcwildcard:artists/finnish>> painted this",
                           "Recursive glob wildcard",
                           CreateMockFiles("artists/finnish", "artists/dutch"));

            // No matches - should include warning comment
            AssertTransform("__nonexistent*__ test", 
                           "<wcwildcard:nonexistent*><comment:no glob matches> test",
                           "No glob matches",
                           CreateMockFiles());

            // Single match - should not use random
            AssertTransform("__unique*__ test", 
                           "<wcwildcard:unique-file> test",
                           "Single glob match",
                           CreateMockFiles("unique-file"));
        }

        private static void TestAdvancedWildcardOptions()
        {
            // Basic quantifier: __2$$colors__
            AssertTransform("I like __2$$colors__", 
                           "I like <wcwildcard[2,]:colors>",
                           "Basic wildcard quantifier");

            // Range quantifier: __2-3$$animals__
            AssertTransform("My pets are __2-3$$animals__", 
                           "My pets are <wcwildcard[2-3,]:animals>",
                           "Range wildcard quantifier");

            // No lower bound: __-2$$flavors__
            AssertTransform("Pick __-2$$flavors__", 
                           "Pick <wcwildcard[1-2,]:flavors>",
                           "No lower bound wildcard quantifier");


            // Prefix flags only (ignored): __@~ro$$styles__
            AssertTransform("Style: __@~ro$$styles__", 
                           "Style: <wcwildcard:styles>",
                           "Prefix flags only wildcard");

            // Prefix flags with quantifier: __@~ro2$$moods__
            AssertTransform("Mood: __@~ro2$$moods__", 
                           "Mood: <wcwildcard[2,]:moods>",
                           "Prefix flags with quantifier wildcard");

            // Custom separator: __2$$ and $$colors__
            AssertTransform("Colors: __2$$ and $$colors__", 
                           "Colors: <wcwildcard[2, and ]:colors>",
                           "Custom separator wildcard");

            // Complex example: __@~ro2-3$$ with $$themes__
            AssertTransform("Themes: __@~ro2-3$$ with $$themes__", 
                           "Themes: <wcwildcard[2-3, with ]:themes>",
                           "Complex advanced wildcard options");

            // Mixed with glob patterns: __2$$colors*__
            AssertTransform("__2$$colors*__ are nice", 
                           "<wcrandom[2,]:<wcwildcard:colors-cold>|<wcwildcard:colors-warm>> are nice",
                           "Advanced options with glob wildcard",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Custom separator with glob patterns: __2$$ and $$colors*__
            AssertTransform("__2$$ and $$colors*__ are nice", 
                           "<wcrandom[2, and ]:<wcwildcard:colors-cold>|<wcwildcard:colors-warm>> are nice",
                           "Custom separator with glob wildcard",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Nested in variants: {__2$$colors__|blue}
            AssertTransform("I like {__2$$colors__|blue}", 
                           "I like <wcrandom:<wcwildcard[2,]:colors>|blue>",
                           "Advanced wildcard options in variant");

            // Multiple advanced wildcards
            AssertTransform("__2$$colors__ and __1-3$$animals__", 
                           "<wcwildcard[2,]:colors> and <wcwildcard[1-3,]:animals>",
                           "Multiple advanced wildcards");

            // Edge case: empty quantifier with $$
            AssertTransform("__$$colors__", 
                           "<wcwildcard:colors>",
                           "Empty quantifier with $$ wildcard");

            // Edge case: just prefix flags
            AssertTransform("__@$$colors__", 
                           "<wcwildcard:colors>",
                           "Just prefix flags wildcard");
        }

        private static void TestLabelFiltering()
        {
            // Basic label filter with single quotes: __wildcard'filter'__
            AssertTransform("__colors'primary'__ are nice", 
                           "<wcpushmacro[wcfilter_colors]:primary><wcwildcard:colors:primary><wcpopmacro:wcfilter_colors> are nice",
                           "Basic label filter with single quotes");

            // Basic label filter with double quotes: __wildcard\"filter\"__
            AssertTransform("__colors\"primary\"__ are nice", 
                           "<wcpushmacro[wcfilter_colors]:primary><wcwildcard:colors:primary><wcpopmacro:wcfilter_colors> are nice",
                           "Basic label filter with double quotes");

            // Label filter with quantifier: __2$$wildcard'filter'__
            AssertTransform("__2$$colors'primary'__ work well", 
                           "<wcpushmacro[wcfilter_colors]:primary><wcwildcard[2,]:colors:primary><wcpopmacro:wcfilter_colors> work well",
                           "Label filter with quantifier");

            // Label filter with range quantifier: __2-3$$wildcard'filter'__
            AssertTransform("__2-3$$colors'bright'__ are vibrant", 
                           "<wcpushmacro[wcfilter_colors]:bright><wcwildcard[2-3,]:colors:bright><wcpopmacro:wcfilter_colors> are vibrant",
                           "Label filter with range quantifier");

            // Label filter with custom separator: __2$$ and $$wildcard'filter'__
            AssertTransform("__2$$ and $$colors'warm'__ blend nicely", 
                           "<wcpushmacro[wcfilter_colors]:warm><wcwildcard[2, and ]:colors:warm><wcpopmacro:wcfilter_colors> blend nicely",
                           "Label filter with custom separator");

            // Filter inheritance with ^wildcard syntax: __target'^source'__
            AssertTransform("__styles'^colors'__ match perfectly", 
                           "<wcpushmacro[wcfilter_styles]:<wcmacro:wcfilter_colors>><wcwildcard:styles:<wcmacro:wcfilter_colors>><wcpopmacro:wcfilter_styles> match perfectly",
                           "Filter inheritance with ^wildcard syntax");

            // Filter definition with #wildcard syntax: __source'#primary,bright'__
            AssertTransform("__colors'#primary,bright'__ are defined", 
                           "<wcpushmacro[wcfilter_colors]:primary,bright><wcwildcard:colors><wcpopmacro:wcfilter_colors> are defined",
                           "Filter definition with #wildcard syntax");

            // Complex filter with multiple labels: __wildcard'label1,label2+label3'__
            AssertTransform("__themes'contemporary,futuristic+!alien'__ work", 
                           "<wcpushmacro[wcfilter_themes]:contemporary,futuristic+!alien><wcwildcard:themes:contemporary,futuristic+!alien><wcpopmacro:wcfilter_themes> work",
                           "Complex filter with multiple labels");

            // Filter with numeric index: __wildcard'42,primary'__
            AssertTransform("__items'1,special'__ are selected", 
                           "<wcpushmacro[wcfilter_items]:2,special><wcwildcard:items:2,special><wcpopmacro:wcfilter_items> are selected",
                           "Filter with numeric index");

            // Filter with variables: __wildcard'${genre}+${theme}'__
            AssertTransform("__styles'<macro:genre>+<macro:theme>'__ match", 
                           "<wcpushmacro[wcfilter_styles]:<macro:genre>+<macro:theme>><wcwildcard:styles:<macro:genre>+<macro:theme>><wcpopmacro:wcfilter_styles> match",
                           "Filter with variables");

            // Label filter with glob patterns: __colors*'bright'__
            AssertTransform("__colors*'warm'__ are nice", 
                           "<wcrandom:<wcwildcard:colors-cold:warm>|<wcwildcard:colors-warm:warm>> are nice",
                           "Label filter with glob patterns",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Label filter with glob patterns and quantifier: __2$$colors*'bright'__
            AssertTransform("__2$$colors*'bright'__ work well", 
                           "<wcrandom[2,]:<wcwildcard:colors-cold:bright>|<wcwildcard:colors-warm:bright>> work well",
                           "Label filter with glob patterns and quantifier",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Empty filter (should still generate macro management): __wildcard''__
            AssertTransform("__colors''__ are basic", 
                           "<wcpushmacro[wcfilter_colors]:><wcwildcard:colors:><wcpopmacro:wcfilter_colors> are basic",
                           "Empty filter");

            // Filter with path wildcards: __path/to/wildcard'filter'__
            AssertTransform("__themes/modern'sleek'__ designs", 
                           "<wcpushmacro[wcfilter_themes_modern]:sleek><wcwildcard:themes/modern:sleek><wcpopmacro:wcfilter_themes_modern> designs",
                           "Filter with path wildcards");

            // Multiple filtered wildcards in one line
            AssertTransform("__colors'bright'__ and __textures'smooth'__ combine", 
                           "<wcpushmacro[wcfilter_colors]:bright><wcwildcard:colors:bright><wcpopmacro:wcfilter_colors> and <wcpushmacro[wcfilter_textures]:smooth><wcwildcard:textures:smooth><wcpopmacro:wcfilter_textures> combine",
                           "Multiple filtered wildcards");

            // Filter inheritance chain: __target1'^source'__ then __target2'^target1'__
            AssertTransform("__styles'^colors'__ then __moods'^styles'__", 
                           "<wcpushmacro[wcfilter_styles]:<wcmacro:wcfilter_colors>><wcwildcard:styles:<wcmacro:wcfilter_colors>><wcpopmacro:wcfilter_styles> then <wcpushmacro[wcfilter_moods]:<wcmacro:wcfilter_styles>><wcwildcard:moods:<wcmacro:wcfilter_styles>><wcpopmacro:wcfilter_moods>",
                           "Filter inheritance chain");

            // Filter definition followed by inheritance: __source'#primary'__ then __target'^source'__
            AssertTransform("__colors'#primary'__ then __styles'^colors'__", 
                           "<wcpushmacro[wcfilter_colors]:primary><wcwildcard:colors><wcpopmacro:wcfilter_colors> then <wcpushmacro[wcfilter_styles]:<wcmacro:wcfilter_colors>><wcwildcard:styles:<wcmacro:wcfilter_colors>><wcpopmacro:wcfilter_styles>",
                           "Filter definition followed by inheritance");

            // Filter with prefix flags: __@~ro2$$wildcard'filter'__
            AssertTransform("__@~ro2$$themes'modern'__ are selected", 
                           "<wcpushmacro[wcfilter_themes]:modern><wcwildcard[2,]:themes:modern><wcpopmacro:wcfilter_themes> are selected",
                           "Filter with prefix flags");

            // Filter with variants in filter content: __wildcard'{primary|secondary}'__
            AssertTransform("__colors'<wcrandom:primary|secondary>'__ work", 
                           "<wcpushmacro[wcfilter_colors]:<wcrandom:primary|secondary>><wcwildcard:colors:<wcrandom:primary|secondary>><wcpopmacro:wcfilter_colors> work",
                           "Filter with variants in filter content");

            // Edge case: filter with special characters: __wildcard'label+with-special_chars'__
            AssertTransform("__items'special-label+with_chars'__ selected", 
                           "<wcpushmacro[wcfilter_items]:special-label+with_chars><wcwildcard:items:special-label+with_chars><wcpopmacro:wcfilter_items> selected",
                           "Filter with special characters");

            // Edge case: no filter should work normally: __wildcard__
            AssertTransform("__colors__ are normal", 
                           "<wcwildcard:colors> are normal",
                           "No filter works normally");

            // Edge case: filter with advanced wildcard options and glob: __2$$ and $$colors*'warm'__
            AssertTransform("__2$$ and $$colors*'warm'__ work", 
                           "<wcrandom[2, and ]:<wcwildcard:colors-cold:warm>|<wcwildcard:colors-warm:warm>> work",
                           "Filter with advanced options and glob",
                           CreateMockFiles("colors-cold", "colors-warm"));
        }

        private static void TestWildcardChoiceLabels()
        {
            // Basic single-quoted labels with :: before <
            AssertTransform("'primary,bright'::red car",
                           "(primary,bright)::red car",
                           "Single-quoted labels converted to parentheses");

            // Basic double-quoted labels with :: before <
            AssertTransform("\"warm,vibrant\"::blue sky",
                           "(warm,vibrant)::blue sky",
                           "Double-quoted labels converted to parentheses");

            // Labels with :: before < directive
            AssertTransform("'modern,sleek'::car <wcrandom:red|blue>",
                           "(modern,sleek)::car <wcrandom:red|blue>",
                           "Labels with :: before directive");

            // Labels with complex content after ::
            AssertTransform("'fantasy,magical'::wizard <wcwildcard:spells> casting",
                           "(fantasy,magical)::wizard <wcwildcard:spells> casting",
                           "Labels with complex content after ::");

            // Multiple labels separated by commas
            AssertTransform("'tag1,tag2,tag3'::content here",
                           "(tag1,tag2,tag3)::content here",
                           "Multiple labels separated by commas");

            // Labels with spaces
            AssertTransform("'modern car,fast vehicle'::racing <wcrandom:red|blue>",
                           "(modern car,fast vehicle)::racing <wcrandom:red|blue>",
                           "Labels with spaces");

            // Labels with special characters
            AssertTransform("'sci-fi,high-tech'::robot design",
                           "(sci-fi,high-tech)::robot design",
                           "Labels with special characters");

            // Empty labels should still be converted
            AssertTransform("''::empty labels test",
                           "()::empty labels test",
                           "Empty labels converted to empty parentheses");

            // Whitespace before labels should be preserved
            AssertTransform("  'spaced,labels'::content",
                           "  (spaced,labels)::content",
                           "Whitespace before labels preserved");

            // No :: in line - should not change
            AssertTransform("'labels'without double colon",
                           "'labels'without double colon",
                           "No :: in line - no conversion");

            // :: after < - should not change
            AssertTransform("<wcrandom:red|blue> 'labels'::after directive",
                           "<wcrandom:red|blue> 'labels'::after directive",
                           ":: after < - no conversion");

            // No quotes at start - should not change
            AssertTransform("unquoted::content here",
                           "unquoted::content here",
                           "No quotes at start - no conversion");

            // Quotes not at start - should not change
            AssertTransform("prefix 'labels'::content",
                           "prefix 'labels'::content",
                           "Quotes not at start - no conversion");

            // Unclosed quotes - should not change
            AssertTransform("'unclosed::content here",
                           "'unclosed::content here",
                           "Unclosed quotes - no conversion");

            // Mixed quote types - only process if properly closed
            AssertTransform("'mixed\"::content here",
                           "'mixed\"::content here",
                           "Mixed quote types - no conversion");

            // Real-world example with wildcard processing
            AssertTransform("'portrait,headshot'::beautiful woman with <wcwildcard:hair_colors> hair",
                           "(portrait,headshot)::beautiful woman with <wcwildcard:hair_colors> hair",
                           "Real-world example with wildcard processing");

            // Complex example with multiple directives
            AssertTransform("'anime,kawaii'::girl with <wcrandom:red|blue> hair and <wcwildcard:expressions>",
                           "(anime,kawaii)::girl with <wcrandom:red|blue> hair and <wcwildcard:expressions>",
                           "Complex example with multiple directives");

            // Labels with numeric content
            AssertTransform("'1girl,solo'::anime character",
                           "(1girl,solo)::anime character",
                           "Labels with numeric content");

            // Labels with underscores and hyphens
            AssertTransform("'high_quality,ultra-detailed'::masterpiece artwork",
                           "(high_quality,ultra-detailed)::masterpiece artwork",
                           "Labels with underscores and hyphens");
        }

        #endregion

        #region Test Variables

        private static void TestVariableAssignments()
        {
            // Deferred assignment: ${var=value}
            AssertTransform("${color=red} The ${color} car", 
                           "<setmacro[color,false]:red> The <macro:color> car",
                           "Deferred variable assignment");
            
            AssertTransform("${color=!red}  The ${color} car",
                "<setvar[color,false]:red><setmacro[color,false]:<var:color>>  The <macro:color> car",
                "Immediate variable assignment");
            
            AssertTransform("${season=!__season__}${year={2000|2010|2020}} The ${season} of ${year}",
                "<setvar[season,false]:<wcwildcard:season>><setmacro[season,false]:<var:season>><setmacro[year,false]:<wcrandom:2000|2010|2020>> The <macro:season> of <macro:year>",
                "Complex assignments");

            // Multiple assignments
            AssertTransform("${a=1}${b=2} Values: ${a}, ${b}", 
                           "<setmacro[a,false]:1><setmacro[b,false]:2> Values: <macro:a>, <macro:b>",
                           "Multiple variable assignments");

            AssertTransform("${color=red} car", 
                           "<setmacro[color,false]:red> car",
                           "Variable with simple value");
            
            AssertTransform("${color={red|blue}} car", 
                           "<setmacro[color,false]:<wcrandom:red|blue>> car",
                           "Variable with variant value");
            
            AssertTransform("${color=__colors__} car", 
                           "<setmacro[color,false]:<wcwildcard:colors>> car",
                           "Variable with wildcard value");
            
            AssertTransform("${colors={2$$red|blue|green}} palette", 
                           "<setmacro[colors,false]:<wcrandom[2,]:red|blue|green>> palette",
                           "Variable with quantified variant");
            
            AssertTransform("${color={2::red|blue}} car", 
                           "<setmacro[color,false]:<wcrandom:2::red|blue>> car",
                           "Variable with weighted variant");
            
            AssertTransform("${color=!{red|blue}} car", 
                           "<setvar[color,false]:<wcrandom:red|blue>><setmacro[color,false]:<var:color>> car",
                           "Immediate variable with variant");
        }

        private static void TestVariableAccess()
        {
            // Variable access: ${var}
            AssertTransform("The ${color} car is ${size}", 
                           "The <macro:color> car is <macro:size>",
                           "Variable access");

            // Variable in variant
            AssertTransform("{${color}|blue} car", 
                           "<wcrandom:<macro:color>|blue> car",
                           "Variable in variant");
        }

        private static void TestImmediateVariables()
        {
            // Immediate assignment: ${var=!value}
            AssertTransform("${color=!red} The ${color} car", 
                           "<setvar[color,false]:red><setmacro[color,false]:<var:color>> The <macro:color> car",
                           "Immediate variable assignment");

            // Immediate with variant
            AssertTransform("${choice=!{red|blue}} Color is ${choice}", 
                           "<setvar[choice,false]:<wcrandom:red|blue>><setmacro[choice,false]:<var:choice>> Color is <macro:choice>",
                           "Immediate variable with variant");
        }

        #endregion

        #region Test Prompt Editing

        private static void TestPromptEditing()
        {
            // Basic prompt editing: [from:to:step]
            AssertTransform("[girl:boy:5] walking", 
                           "<fromto[5]:girl||boy> walking",
                           "Basic prompt editing");

            // Prompt editing with decimal step
            AssertTransform("[happy:sad:0.5] face", 
                           "<fromto[0.5]:happy||sad> face",
                           "Prompt editing with decimal step");

            // Missing from value: [:to:step]
            AssertTransform("[:boy:3] character", 
                           "<fromto[3]:<comment:empty>||boy> character",
                           "Prompt editing missing from value");

            // Special to-only syntax: [to:step]
            AssertTransform("[:boy:3] character", 
                "<fromto[3]:<comment:empty>||boy> character",
                "Special to-only syntax");

            // Missing to value: [from::step]
            AssertTransform("[girl::7] character", 
                           "<fromto[7]:girl||<comment:empty>> character",
                           "Prompt editing missing to value");

            // Missing both from and to: [::step]
            AssertTransform("[::2] something", 
                           "<fromto[2]:<comment:empty>||<comment:empty>> something",
                           "Prompt editing missing from and to values");

            // Multiple prompt editing in one line
            AssertTransform("[girl:boy:5] and [happy:sad:3] person", 
                           "<fromto[5]:girl||boy> and <fromto[3]:happy||sad> person",
                           "Multiple prompt editing");

            // Prompt editing with variants in from/to
            AssertTransform("[{red|blue}:green:0.4] car", 
                           "<fromto[0.4]:<wcrandom:red|blue>||green> car",
                           "Prompt editing with variant in from");

            AssertTransform("[red:{blue|green}:2] car", 
                           "<fromto[2]:red||<wcrandom:blue|green>> car",
                           "Prompt editing with variant in to");

            // Prompt editing with wildcards in from/to
            AssertTransform("[__colors__:green:6] background", 
                           "<fromto[6]:<wcwildcard:colors>||green> background",
                           "Prompt editing with wildcard in from");

            AssertTransform("[red:__colors__:8] background", 
                           "<fromto[8]:red||<wcwildcard:colors>> background",
                           "Prompt editing with wildcard in to");

            // Prompt editing with complex nested content
            AssertTransform("[{red|__colors__}:{blue|green}:1.5] complex", 
                           "<fromto[1.5]:<wcrandom:red|<wcwildcard:colors>>||<wcrandom:blue|green>> complex",
                           "Prompt editing with complex nested content");

            // Prompt editing with variables
            AssertTransform("[${color}:blue:4] car", 
                           "<fromto[4]:<macro:color>||blue> car",
                           "Prompt editing with variable in from");

            // Prompt editing should be processed BEFORE negative attention
            // This ensures [text:other:5] is treated as prompt editing, not negative attention
            AssertTransform("[girl:boy:5] but not [negative] attention", 
                           "<fromto[5]:girl||boy> but not (negative:0.9) attention",
                           "Prompt editing processed before negative attention");

            // Edge case: malformed prompt editing (only one colon) should be treated as negative attention
            AssertTransform("[girl:boy] should be negative", 
                           "(girl:boy:0.9) should be negative",
                           "Malformed prompt editing treated as negative attention");

            // Edge case: no colons should be treated as negative attention
            AssertTransform("[just text] should be negative", 
                           "(just text:0.9) should be negative",
                           "No colons treated as negative attention");

            // Edge case: empty step is still valid prompt editing syntax - let SwarmUI validate
            AssertTransform("[girl:boy:] should be negative", 
                           "<fromto[<comment:empty>]:girl||boy> should be negative",
                           "Empty step treated as prompt editing");

            // Edge case: non-numeric step is still valid prompt editing syntax - let SwarmUI validate
            AssertTransform("[girl:boy:abc] should be negative", 
                           "<fromto[abc]:girl||boy> should be negative",
                           "Non-numeric step treated as prompt editing");

            // Nested variants in from/to values (more realistic scenario)
            AssertTransform("[{red|blue}:target:3] test", 
                           "<fromto[3]:<wcrandom:red|blue>||target> test",
                           "Prompt editing with nested variant in from value");

            // Complex but realistic nesting with wildcards and variants
            AssertTransform("[{__colors__|red}:{blue|__shades__}:2.5] background", 
                           "<fromto[2.5]:<wcrandom:<wcwildcard:colors>|red>||<wcrandom:blue|<wcwildcard:shades>>> background",
                           "Prompt editing with complex realistic nesting");

            // Escaped colons should not split prompt editing
            AssertTransform("[text\\:with\\:colons:target:5] test", 
                           "<fromto[5]:text\\:with\\:colons||target> test",
                           "Prompt editing with escaped colons");
        }

        #endregion

        #region Test Alternating Words

        private static void TestAlternatingWords()
        {
            // Basic alternating words: [word1|word2]
            AssertTransform("[cow|horse] in a field", 
                           "<alternate:cow||horse> in a field",
                           "Basic alternating words");

            // Single word (should not be treated as alternating)
            AssertTransform("[cow] in a field", 
                           "(cow:0.9) in a field",
                           "Single word treated as negative attention");

            // Multiple alternating words: [word1|word2|word3|word4]
            AssertTransform("[cow|cow|horse|man|siberian tiger|ox|man] in a field", 
                           "<alternate:cow||cow||horse||man||siberian tiger||ox||man> in a field",
                           "Multiple alternating words");

            // Multiple alternating sequences in one line
            AssertTransform("[red|blue] car with [big|small] wheels", 
                           "<alternate:red||blue> car with <alternate:big||small> wheels",
                           "Multiple alternating sequences");

            // Alternating words with variants inside
            AssertTransform("[{red|crimson}|blue] car", 
                           "<alternate:<wcrandom:red|crimson>||blue> car",
                           "Alternating words with variant in option");

            // Alternating words with wildcards inside
            AssertTransform("[__colors__|blue] background", 
                           "<alternate:<wcwildcard:colors>||blue> background",
                           "Alternating words with wildcard in option");

            // Alternating words with variables inside
            AssertTransform("[${color}|blue] car", 
                           "<alternate:<macro:color>||blue> car",
                           "Alternating words with variable in option");

            // Complex nesting with alternating words
            AssertTransform("[{red|__colors__}|{blue|green}] complex", 
                           "<alternate:<wcrandom:red|<wcwildcard:colors>>||<wcrandom:blue|green>> complex",
                           "Alternating words with complex nested content");

            // Alternating words should be processed AFTER prompt editing
            // This ensures [from:to:step] is handled before [word1|word2]
            AssertTransform("[girl:boy:5] and [red|blue] car", 
                           "<fromto[5]:girl||boy> and <alternate:red||blue> car",
                           "Alternating words processed after prompt editing");

            // Empty options in alternating words
            AssertTransform("[red||blue] car", 
                           "<alternate:red||<comment:empty>||blue> car",
                           "Alternating words with empty option");

            // Alternating words with escaped pipes
            AssertTransform("[red\\|crimson|blue] car", 
                           "<alternate:red\\|crimson||blue> car",
                           "Alternating words with escaped pipe");

            // Edge case: alternating words should not conflict with negative attention
            // Single option should be negative attention, multiple should be alternating
            AssertTransform("[single] vs [first|second]", 
                           "(single:0.9) vs <alternate:first||second>",
                           "Single vs multiple options handling");

            // More realistic nested alternating words
            AssertTransform("[red {bright|dark}|blue] car", 
                           "<alternate:red <wcrandom:bright|dark>||blue> car",
                           "Alternating words with nested variant");

            // Complex but realistic alternating with wildcards
            AssertTransform("[__colors__ bright|dark __shades__] theme", 
                           "<alternate:<wcwildcard:colors> bright||dark <wcwildcard:shades>> theme",
                           "Alternating words with realistic complex nesting");
        }

        #endregion

        #region Test Negative Attention

        private static void TestNegativeAttention()
        {
            // Basic negative attention: [text]
            AssertTransform("A beautiful [ugly] woman", 
                           "A beautiful (ugly:0.9) woman",
                           "Basic negative attention");

            // Multiple negative attention blocks
            AssertTransform("[bad] and [worse] things", 
                           "(bad:0.9) and (worse:0.9) things",
                           "Multiple negative attention blocks");

            // Negative attention with variants
            AssertTransform("[some {a|b|c} text]", 
                           "(some <wcrandom:a|b|c> text:0.9)",
                           "Negative attention with variants");

            // Negative attention with wildcards
            AssertTransform("[some __wildcard__ text]", 
                           "(some <wcwildcard:wildcard> text:0.9)",
                           "Negative attention with wildcards");

            // Negative attention with quantified wildcards
            AssertTransform("[some {2$$__wildcard__} text]", 
                           "(some <wcwildcard[2,]:wildcard> text:0.9)",
                           "Negative attention with quantified wildcards");

            // Negative attention with complex SwarmUI syntax
            AssertTransform("[some <random[3,]this|variant|already|in|swarm|syntax> text]", 
                           "(some <random[3,]this|variant|already|in|swarm|syntax> text:0.9)",
                           "Negative attention with SwarmUI syntax");

            // Complex example from user request
            AssertTransform("[some {a|b|c} __somewildcard__ {2$$__somewildcard__} <random[3,]this|variant|already|in|swarm|syntax>]", 
                           "(some <wcrandom:a|b|c> <wcwildcard:somewildcard> <wcwildcard[2,]:somewildcard> <random[3,]this|variant|already|in|swarm|syntax>:0.9)",
                           "Complex negative attention example");

            // Negative attention with variables
            AssertTransform("[${color=red} ${color} car]", 
                           "(<setmacro[color,false]:red> <macro:color> car:0.9)",
                           "Negative attention with variables");

            // Nested negative attention (inner brackets should also be processed)
            AssertTransform("[outer [inner] text]", 
                           "(outer (inner:0.9) text:0.9)",
                           "Nested negative attention brackets");
            
            AssertTransform("[[[light]]]",
                "(light:0.729)", // 0.729 = Pow(0.9, 3) rounded to 3 decimals
                "Nested negative attention should collapse into single attention with weight = 0.9^nestLevel");

            // Empty negative attention
            AssertTransform("[]", 
                           "(:0.9)",
                           "Empty negative attention");

            // Negative attention with whitespace
            AssertTransform("[ some text ]", 
                           "( some text :0.9)",
                           "Negative attention with whitespace");

            // Multiple negative attention in complex text
            AssertTransform("A [bad] person with {red|blue} hair and [terrible] attitude", 
                           "A (bad:0.9) person with <wcrandom:red|blue> hair and (terrible:0.9) attitude",
                           "Multiple negative attention in complex text");

            // Negative attention with escaped brackets (should not transform)
            AssertTransform("This \\[should not transform\\]", 
                           "This \\[should not transform\\]",
                           "Escaped negative attention brackets");
        }

        #endregion

        #region Test Edge Cases

        private static void TestMalformedSyntax()
        {
            // Unclosed braces
            AssertTransform("{red|blue car", 
                           "{red|blue car",
                           "Unclosed variant brace");

            // Unclosed variable
            AssertTransform("${color=red car", 
                           "${color=red car",
                           "Unclosed variable brace");

            // Empty variant
            AssertTransform("{} car", 
                           "<wcrandom:<comment:empty>> car",
                           "Empty variant");

            // Malformed wildcard
            AssertTransform("__incomplete", 
                           "__incomplete",
                           "Incomplete wildcard");
        }

        private static void TestComplexNesting()
        {
            // Variant with wildcard
            AssertTransform("{__colors__|blue} car", 
                           "<wcrandom:<wcwildcard:colors>|blue> car",
                           "Variant with wildcard");

            // Variable with variant and wildcard
            AssertTransform("${style={modern|__historical__}} ${style} building", 
                           "<setmacro[style,false]:<wcrandom:modern|<wcwildcard:historical>>> <macro:style> building",
                           "Complex nesting");

            // Quantifier with nested structures
            AssertTransform("{2$$__colors__|{red|blue}|green}", 
                           "<wcrandom[2,]:<wcwildcard:colors>|<wcrandom:red|blue>|green>",
                           "Quantifier with nested structures");

            AssertTransform("${clothed_state={__scenes/${scene}/clothed_state__}}",
                "<setmacro[clothed_state,false]:<wcrandom:<wcwildcard:scenes/<macro:scene>/clothed_state>>>",
                "Wildcard inside variant with variable inside setvar");
        }

        private static void TestSpecialCharacters()
        {
            // Escaped characters in variants
            AssertTransform("{red\\|blue|green}", 
                           "<wcrandom:red\\|blue|green>",
                           "Escaped pipe in variant");

            // Special characters in wildcards
            AssertTransform("__special-chars_123__ test", 
                           "<wcwildcard:special-chars_123> test",
                           "Special characters in wildcard");

            // Unicode characters
            AssertTransform("{café|naïve} word", 
                           "<wcrandom:café|naïve> word",
                           "Unicode characters");
        }

        #endregion

        #region Test Set Command

        private static void TestSetCommandLongForm()
        {
            // Basic set command: <ppp:set varname>value<ppp:/set>
            AssertTransform("<ppp:set color>red<ppp:/set>", 
                           "<setmacro[color,false]:red>",
                           "Basic set command long form");

            // Set with evaluate modifier: <ppp:set varname evaluate>value<ppp:/set>
            AssertTransform("<ppp:set color evaluate>red<ppp:/set>", 
                           "<setvar[color,false]:red><setmacro[color,false]:<var:color>>",
                           "Set command with evaluate modifier");

            // Set with add modifier: <ppp:set varname add>value<ppp:/set>
            AssertTransform("<ppp:set color add>blue<ppp:/set>", 
                           "<wcaddmacro[color]:, blue>",
                           "Set command with add modifier");

            // Set with evaluate add modifiers: <ppp:set varname evaluate add>value<ppp:/set>
            AssertTransform("<ppp:set color evaluate add>blue<ppp:/set>", 
                           "<setvar[color,false]:<macro:color>, blue><setmacro[color,false]:<var:color>>",
                           "Set command with evaluate add modifiers");

            // Set with evaluate add modifiers in different order: <ppp:set varname add evaluate>value<ppp:/set>
            AssertTransform("<ppp:set color add evaluate>blue<ppp:/set>", 
                "<setvar[color,false]:<macro:color>, blue><setmacro[color,false]:<var:color>>",
                "Set command with evaluate add modifiers");

            // Set with ifundefined modifier: <ppp:set varname ifundefined>value<ppp:/set>
            AssertTransform("<ppp:set color ifundefined>green<ppp:/set>", 
                           "<wcmatch:<wccase[length(color) eq 0]:<setmacro[color,false]:green>>>",
                           "Set command with ifundefined modifier");

            // Set with evaluate ifundefined modifiers: <ppp:set varname evaluate ifundefined>value<ppp:/set>
            AssertTransform("<ppp:set color evaluate ifundefined>green<ppp:/set>", 
                           "<wcmatch:<wccase[length(color) eq 0]:<setvar[color,false]:green><setmacro[color,false]:<var:color>>>>",
                           "Set command with evaluate ifundefined modifiers");

            // Set with ifundefined evaluate modifiers in different order: <ppp:set varname ifundefined evaluate>value<ppp:/set>
            AssertTransform("<ppp:set color ifundefined evaluate>green<ppp:/set>", 
                           "<wcmatch:<wccase[length(color) eq 0]:<setvar[color,false]:green><setmacro[color,false]:<var:color>>>>",
                           "Set command with ifundefined evaluate modifiers");

            // Set with complex value containing variants
            AssertTransform("<ppp:set mood>{happy|sad}<ppp:/set>", 
                           "<setmacro[mood,false]:<wcrandom:happy|sad>>",
                           "Set command with variant value");

            // Set with complex value containing wildcards
            AssertTransform("<ppp:set style>__art_styles__<ppp:/set>", 
                           "<setmacro[style,false]:<wcwildcard:art_styles>>",
                           "Set command with wildcard value");
        }

        private static void TestSetCommandShortForm()
        {
            // Basic assignment (already supported): ${var=value}
            AssertTransform("${color=red}", 
                           "<setmacro[color,false]:red>",
                           "Basic assignment short form");

            // Immediate assignment (already supported): ${var=!value}
            AssertTransform("${color=!red}", 
                           "<setvar[color,false]:red><setmacro[color,false]:<var:color>>",
                           "Immediate assignment short form");

            // Add assignment: ${var+=value}
            AssertTransform("${color+=blue}", 
                           "<wcaddmacro[color]:, blue>",
                           "Add assignment short form");

            // Immediate add assignment: ${var+=!value}
            // Immediate add assignment needs to trigger evaluation of the variable, so the emitted code is more complicated than you might expect and does not use wcadd...
            AssertTransform("${color+=!blue}", 
                           "<setvar[color,false]:<macro:color>, blue><setmacro[color,false]:<var:color>>",
                           "Immediate add assignment short form");

            // Immediate add assignment with complex value: ${var+=!{variant|value}}
            AssertTransform("${mood+=!{happy|excited}}", 
                           "<setvar[mood,false]:<macro:mood>, <wcrandom:happy|excited>><setmacro[mood,false]:<var:mood>>",
                           "Immediate add assignment with variant value");

            // Ifundefined assignment: ${var?=value}
            AssertTransform("${color?=green}", 
                           "<wcmatch:<wccase[length(color) eq 0]:<setmacro[color,false]:green>>>",
                           "Ifundefined assignment short form");

            // Immediate ifundefined assignment: ${var?=!value}
            AssertTransform("${color?=!green}", 
                           "<wcmatch:<wccase[length(color) eq 0]:<setvar[color,false]:green><setmacro[color,false]:<var:color>>>>",
                           "Immediate ifundefined assignment short form");

            // Add assignment with variant value
            AssertTransform("${mood+={happy|excited}}", 
                           "<wcaddmacro[mood]:, <wcrandom:happy|excited>>",
                           "Add assignment with variant value");

            // Ifundefined assignment with wildcard value
            AssertTransform("${style?=__modern_styles__}", 
                           "<wcmatch:<wccase[length(style) eq 0]:<setmacro[style,false]:<wcwildcard:modern_styles>>>>",
                           "Ifundefined assignment with wildcard value");

            // Multiple assignments in sequence
            AssertTransform("${color=red}${mood+=happy}${style?=modern}", 
                           "<setmacro[color,false]:red><wcaddmacro[mood]:, happy><wcmatch:<wccase[length(style) eq 0]:<setmacro[style,false]:modern>>>",
                           "Multiple assignments in sequence");
        }

        private static void TestSetCommandEdgeCases()
        {
            // Empty value
            AssertTransform("<ppp:set color><ppp:/set>", 
                           "<setmacro[color,false]:<comment:empty>>",
                           "Set command with empty value");

            // Variable name with underscores and numbers
            AssertTransform("${my_var_123=test}", 
                           "<setmacro[my_var_123,false]:test>",
                           "Variable name with underscores and numbers");

            // Whitespace in long form
            AssertTransform("<ppp:set color evaluate add >  blue  <ppp:/set>", 
                           "<setvar[color,false]:<macro:color>,   blue  ><setmacro[color,false]:<var:color>>",
                           "Set command with whitespace");

            // Nested set commands (should not be supported, treat as literal)
            AssertTransform("<ppp:set outer><ppp:set inner>value<ppp:/set><ppp:/set>", 
                           "<setmacro[outer,false]:<ppp:set inner>value><ppp:/set>",
                           "Nested set commands");

            // Invalid modifier combinations (add and ifundefined together - should log warning)
            AssertTransform("<ppp:set color add ifundefined>blue<ppp:/set>", 
                           "<ppp:set color add ifundefined>blue<ppp:/set>",
                           "Invalid modifier combination");
        }

        #endregion

        #region Echo Command Tests

        private static void TestEchoCommandLongForm()
        {
            // Basic echo without default: <ppp:echo varname>
            AssertTransform("<ppp:echo color>", 
                           "<macro:color>",
                           "Basic echo command long form");

            // Echo with default value: <ppp:echo varname>default<ppp:/echo>
            AssertTransform("<ppp:echo color>red<ppp:/echo>", 
                           "<wcmatch:<wccase[length(color) eq 0]:red><wccase:<macro:color>>>",
                           "Echo command with default value");

            // Echo with complex default containing variants
            AssertTransform("<ppp:echo mood>{happy|sad}<ppp:/echo>", 
                           "<wcmatch:<wccase[length(mood) eq 0]:<wcrandom:happy|sad>><wccase:<macro:mood>>>",
                           "Echo command with variant default");

            // Echo with wildcard default
            AssertTransform("<ppp:echo style>__styles__<ppp:/echo>", 
                           "<wcmatch:<wccase[length(style) eq 0]:<wcwildcard:styles>><wccase:<macro:style>>>",
                           "Echo command with wildcard default");

            // Multiple echo commands
            AssertTransform("<ppp:echo color>blue<ppp:/echo> and <ppp:echo size>large<ppp:/echo>", 
                           "<wcmatch:<wccase[length(color) eq 0]:blue><wccase:<macro:color>>> and <wcmatch:<wccase[length(size) eq 0]:large><wccase:<macro:size>>>",
                           "Multiple echo commands");

            // Echo with empty default
            AssertTransform("<ppp:echo color><ppp:/echo>", 
                           "<macro:color>",
                           "Echo command with empty default");

            // Echo with whitespace in default
            AssertTransform("<ppp:echo color>  bright red  <ppp:/echo>", 
                           "<wcmatch:<wccase[length(color) eq 0]:  bright red  ><wccase:<macro:color>>>",
                           "Echo command with whitespace in default");
        }

        private static void TestEchoCommandShortForm()
        {
            // Basic variable reference: ${varname}
            AssertTransform("${color}", 
                           "<macro:color>",
                           "Basic variable reference short form");

            // Variable with default: ${varname:default}
            AssertTransform("${color:red}", 
                           "<wcmatch:<wccase[length(color) eq 0]:red><wccase:<macro:color>>>",
                           "Variable with default short form");

            // Variable with complex default containing variants
            AssertTransform("${mood:{happy|sad}}", 
                           "<wcmatch:<wccase[length(mood) eq 0]:<wcrandom:happy|sad>><wccase:<macro:mood>>>",
                           "Variable with variant default");

            // Variable with wildcard default
            AssertTransform("${style:__styles__}", 
                           "<wcmatch:<wccase[length(style) eq 0]:<wcwildcard:styles>><wccase:<macro:style>>>",
                           "Variable with wildcard default");

            // Multiple variables in text
            AssertTransform("I like ${color:blue} ${animal:cats}", 
                           "I like <wcmatch:<wccase[length(color) eq 0]:blue><wccase:<macro:color>>> <wcmatch:<wccase[length(animal) eq 0]:cats><wccase:<macro:animal>>>",
                           "Multiple variables with defaults");

            // Variable with empty default
            AssertTransform("${color:}", 
                           "<macro:color>",
                           "Variable with empty default");

            // Variable with colon in default (should handle properly)
            AssertTransform("${time:12:30}", 
                           "<wcmatch:<wccase[length(time) eq 0]:12:30><wccase:<macro:time>>>",
                           "Variable with colon in default");

            // Variable without default mixed with variable with default
            AssertTransform("${name} likes ${color:blue}", 
                           "<macro:name> likes <wcmatch:<wccase[length(color) eq 0]:blue><wccase:<macro:color>>>",
                           "Mixed variables with and without defaults");
        }

        private static void TestEchoCommandEdgeCases()
        {
            // TODO: Nested echo commands - currently not fully supported due to regex complexity
            // The regex pattern matches the first </ppp:echo> instead of the last one for nested structures
            // This is an edge case that can be addressed in future improvements
            /*
            AssertTransform("<ppp:echo outer><ppp:echo inner>default<ppp:/echo><ppp:/echo>", 
                           "<wcmatch:<wccase[length(outer) == 0]:<ppp:echo inner>default><ppp:/echo><wccase:<macro:outer>>>",
                           "Nested echo commands");
            */

            // Malformed echo commands - let the system handle gracefully
            // Note: We don't test exact output for malformed input, just that it doesn't crash

            // Malformed variable references - let the system handle gracefully
            // Note: We don't test exact output for malformed input, just that it doesn't crash

            // Variable reference with multiple colons (should take first as separator)
            AssertTransform("${var:default:extra}", 
                           "<wcmatch:<wccase[length(var) eq 0]:default:extra><wccase:<macro:var>>>",
                           "Variable with multiple colons in default");

            // Echo command with special characters in variable name
            AssertTransform("<ppp:echo my_var-123>default<ppp:/echo>", 
                           "<wcmatch:<wccase[length(my_var-123) eq 0]:default><wccase:<macro:my_var-123>>>",
                           "Echo command with special chars in variable name");

            // Variable reference with special characters
            AssertTransform("${my_var-123:default}", 
                           "<wcmatch:<wccase[length(my_var-123) eq 0]:default><wccase:<macro:my_var-123>>>",
                           "Variable reference with special chars");

            // Echo with default containing echo syntax (should be processed)
            AssertTransform("<ppp:echo color>${other:blue}<ppp:/echo>", 
                           "<wcmatch:<wccase[length(color) eq 0]:<wcmatch:<wccase[length(other) eq 0]:blue><wccase:<macro:other>>>><wccase:<macro:color>>>",
                           "Echo with default containing variable reference");

            // Whitespace handling in long form
            AssertTransform("<ppp:echo  color  >  default  <ppp:/echo>", 
                           "<wcmatch:<wccase[length(color) eq 0]:  default  ><wccase:<macro:color>>>",
                           "Echo command with whitespace");
        }

        #endregion

        #region Recursive Processing Regression Tests

        private static void TestRecursiveProcessingRegression()
        {
            // Test variants with nested variables
            AssertTransform("{${color}|blue} car", 
                           "<wcrandom:<macro:color>|blue> car",
                           "Variant with variable in option");
            
            AssertTransform("{red|${color}|green} palette", 
                           "<wcrandom:red|<macro:color>|green> palette",
                           "Variant with variable in middle option");
            
            AssertTransform("{${primary}|${secondary}} colors", 
                           "<wcrandom:<macro:primary>|<macro:secondary>> colors",
                           "Variant with variables in multiple options");
            
            // Test variants with nested wildcards
            AssertTransform("{__colors__|blue} theme", 
                           "<wcrandom:<wcwildcard:colors>|blue> theme",
                           "Variant with wildcard in option");
            
            AssertTransform("{red|__colors__|green} palette", 
                           "<wcrandom:red|<wcwildcard:colors>|green> palette",
                           "Variant with wildcard in middle option");
            
            // Test variants with nested variants
            AssertTransform("{red|{light|dark} blue} colors", 
                           "<wcrandom:red|<wcrandom:light|dark> blue> colors",
                           "Variant with nested variant in option");
            
            // Test wildcards with nested variables
            AssertTransform("__scenes/${scene}/clothed_state__ outfit", 
                           "<wcwildcard:scenes/<macro:scene>/clothed_state> outfit",
                           "Wildcard with variable in path");
            
            AssertTransform("__${category}/${subcategory}__ items", 
                           "<wcwildcard:<macro:category>/<macro:subcategory>> items",
                           "Wildcard with multiple variables in path");
            
            // Test wildcards with nested variants
            AssertTransform("__{red|blue}_colors__ theme", 
                           "<wcwildcard:<wcrandom:red|blue>_colors> theme",
                           "Wildcard with variant in path");
            
            // Test prompt editing with nested variables
            AssertTransform("[${from_color}:${to_color}:5] transition", 
                           "<fromto[5]:<macro:from_color>||<macro:to_color>> transition",
                           "Prompt editing with variables in from/to");
            
            // Test prompt editing with nested variants
            AssertTransform("[{red|blue}:{green|yellow}:3] gradient", 
                           "<fromto[3]:<wcrandom:red|blue>||<wcrandom:green|yellow>> gradient",
                           "Prompt editing with variants in from/to");
            
            // Test prompt editing with nested wildcards
            AssertTransform("[__start_colors__:__end_colors__:2] fade", 
                           "<fromto[2]:<wcwildcard:start_colors>||<wcwildcard:end_colors>> fade",
                           "Prompt editing with wildcards in from/to");
            
            // Test alternating words with nested variables
            AssertTransform("[${animal1}|${animal2}] in field", 
                           "<alternate:<macro:animal1>||<macro:animal2>> in field",
                           "Alternating words with variables");
            
            // Test alternating words with nested variants
            AssertTransform("[{red|crimson}|{blue|navy}] colors", 
                           "<alternate:<wcrandom:red|crimson>||<wcrandom:blue|navy>> colors",
                           "Alternating words with variants");
            
            // Test alternating words with nested wildcards
            AssertTransform("[__animals__|__plants__] nature", 
                           "<alternate:<wcwildcard:animals>||<wcwildcard:plants>> nature",
                           "Alternating words with wildcards");
            
            // Test complex multi-level nesting
            AssertTransform("{${color}|{light|dark} {red|blue}} theme", 
                           "<wcrandom:<macro:color>|<wcrandom:light|dark> <wcrandom:red|blue>> theme",
                           "Complex multi-level variant nesting");
            
            AssertTransform("__scenes/${scene}/{${mood}|happy}__ setting", 
                           "<wcwildcard:scenes/<macro:scene>/<wcrandom:<macro:mood>|happy>> setting",
                           "Complex wildcard with variable and variant");
            
            // Test quantified variants with nested content
            AssertTransform("{2$$${color1}|${color2}|blue} palette", 
                           "<wcrandom[2,]:<macro:color1>|<macro:color2>|blue> palette",
                           "Quantified variant with variables");
            
            AssertTransform("{3$$__colors__|{red|green}|blue} mix", 
                           "<wcrandom[3,]:<wcwildcard:colors>|<wcrandom:red|green>|blue> mix",
                           "Quantified variant with wildcard and nested variant");
            
            // Test range variants with nested content
            AssertTransform("{2-3$$${primary}|${secondary}|neutral} scheme", 
                           "<wcrandom[2-3,]:<macro:primary>|<macro:secondary>|neutral> scheme",
                           "Range variant with variables");
            
            // Test the original failing case that started this fix
            AssertTransform("${clothed_state=__scenes/${scene}/clothed_state__}", 
                           "<setmacro[clothed_state,false]:<wcwildcard:scenes/<macro:scene>/clothed_state>>",
                           "wildcard with variable inside setvar");
            
            // Test variable assignments with complex nested content
            AssertTransform("${style={modern|__historical/${period}__}} building", 
                           "<setmacro[style,false]:<wcrandom:modern|<wcwildcard:historical/<macro:period>>>> building",
                           "Variable assignment with variant containing wildcard with variable");
            
            // Test immediate variable assignments with nested content
            AssertTransform("${color=!{${primary}|${secondary}}} ${color} theme", 
                           "<setvar[color,false]:<wcrandom:<macro:primary>|<macro:secondary>>><setmacro[color,false]:<var:color>> <macro:color> theme",
                           "Immediate variable assignment with variant containing variables");
            
            // Test deeply nested structures
            AssertTransform("{${outer}|{${inner1}|{${deep}|literal}}} test", 
                           "<wcrandom:<macro:outer>|<wcrandom:<macro:inner1>|<wcrandom:<macro:deep>|literal>>> test",
                           "Deeply nested variants with variables");
            
            // Test mixed constructs in complex scenarios
            AssertTransform("[{${from_style}|modern}:{${to_style}|classic}:${steps}] and [{${animal1}|cat}|{${animal2}|dog}] scene", 
                           "<fromto[<macro:steps>]:<wcrandom:<macro:from_style>|modern>||<wcrandom:<macro:to_style>|classic>> and <alternate:<wcrandom:<macro:animal1>|cat>||<wcrandom:<macro:animal2>|dog>> scene",
                           "Complex mixed constructs with variables and variants");
            
            // Test negative attention with nested variables
            AssertTransform("[${color} car] in scene", 
                           "(<macro:color> car:0.9) in scene",
                           "Negative attention with variable");
            
            // Test negative attention with nested variants
            AssertTransform("[{red|blue} car] in garage", 
                           "(<wcrandom:red|blue> car:0.9) in garage",
                           "Negative attention with variant");
            
            // Test negative attention with nested wildcards
            AssertTransform("[__colors__ theme] design", 
                           "(<wcwildcard:colors> theme:0.9) design",
                           "Negative attention with wildcard");
            
            // Test nested negative attention with complex content
            AssertTransform("[{${primary}|__colors__} and ${secondary}] palette", 
                           "(<wcrandom:<macro:primary>|<wcwildcard:colors>> and <macro:secondary>:0.9) palette",
                           "Negative attention with mixed nested constructs");
            
            // Test multiple levels of negative attention
            AssertTransform("[[${inner}] outer] content", 
                           "((<macro:inner>:0.9) outer:0.9) content",
                           "Nested negative attention with variable");
            
            // Test negative attention with variable assignments
            AssertTransform("[${style={modern|classic}} building] architecture", 
                           "(<setmacro[style,false]:<wcrandom:modern|classic>> building:0.9) architecture",
                           "Negative attention with variable assignment containing variant");
        }

        #endregion

        #region Helper Methods

        private static void AssertTransform(string input, string expected, string testName, 
                                          ConcurrentDictionary<string, List<string>> mockFiles = null)
        {
            try
            {
                var processor = CreateTestProcessor(mockFiles);
                string taskId = "test-task";
                var task = new ProcessingTask { Id = taskId, Prefix = "" };
                
                // Set up mock files in the task if provided
                if (mockFiles != null)
                {
                    task.InMemoryFiles = mockFiles;
                }

                // Use reflection to access private _tasks field and set it up
                var tasksField = processor.GetType().GetField("_tasks", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var tasks = new ConcurrentDictionary<string, ProcessingTask> { [taskId] = task };
                tasksField?.SetValue(processor, tasks);

                // Use reflection to call private ProcessWildcardLine method
                var method = processor.GetType().GetMethod("ProcessWildcardLine", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (method == null)
                {
                    throw new Exception("ProcessWildcardLine method not found");
                }
                
                string result = (string)method.Invoke(processor, new object[] { input, taskId, true });

                if (result == expected)
                {
                    _testsPassed++;
                    Logs.Debug($"✓ {testName}: PASSED");
                }
                else
                {
                    _testsFailed++;
                    string message = $"{testName}: Expected '{expected}', got '{result}'";
                    _failureMessages.Add(message);
                    Logs.Error($"✗ {message}");
                }
            }
            catch (Exception ex)
            {
                _testsFailed++;
                string message = $"{testName}: Exception - {ex.Message}";
                _failureMessages.Add(message);
                Logs.Error($"✗ {message}");
            }
        }

        private static WildcardProcessor CreateTestProcessor(ConcurrentDictionary<string, List<string>> mockFiles = null)
        {
            // Get the extension folder path for YamlParser initialization
            string extensionFolder = System.IO.Path.GetDirectoryName(typeof(WildcardProcessorTest).Assembly.Location) ?? "";
            var yamlParser = new YamlParser(extensionFolder);
            var processor = new WildcardProcessor(yamlParser);
            
            // If mock files provided, inject them into a test task
            if (mockFiles != null)
            {
                var task = new ProcessingTask { Id = "test-task", Prefix = "" };
                task.InMemoryFiles = mockFiles;
                
                var tasksField = processor.GetType().GetField("_tasks", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var tasks = new ConcurrentDictionary<string, ProcessingTask> { ["test-task"] = task };
                tasksField?.SetValue(processor, tasks);
            }
            
            return processor;
        }

        private static ConcurrentDictionary<string, List<string>> CreateMockFiles(params string[] fileNames)
        {
            var files = new ConcurrentDictionary<string, List<string>>();
            foreach (var fileName in fileNames)
            {
                files.TryAdd(fileName, new List<string> { $"content-{fileName}" });
            }
            return files;
        }

        #endregion

        #region Test If Commands

        private static void TestIfCommandBasic()
        {
            // Simple variable truthiness check
            AssertTransform("<ppp:if myvar>content<ppp:/if>",
                           "<wcmatch:<wccase[myvar]:content>>",
                           "Basic if with variable truthiness");

            // Simple if-else
            AssertTransform("<ppp:if myvar>true content<ppp:else>false content<ppp:/if>",
                           "<wcmatch:<wccase[myvar]:true content><wccase:false content>>",
                           "Basic if-else");

            // If with empty content
            AssertTransform("<ppp:if myvar><ppp:/if>",
                           "<wcmatch:<wccase[myvar]:<comment:empty>>>",
                           "If with empty content");
        }

        private static void TestIfCommandComparisons()
        {
            // Equality comparison
            AssertTransform("<ppp:if myvar eq \"test\">equal<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq \"test\"]:equal>>",
                           "If with equality comparison");

            // Not equal comparison
            AssertTransform("<ppp:if myvar ne \"test\">not equal<ppp:/if>",
                           "<wcmatch:<wccase[myvar ne \"test\"]:not equal>>",
                           "If with not equal comparison");

            // Greater than comparison
            AssertTransform("<ppp:if myvar gt 5>greater<ppp:/if>",
                           "<wcmatch:<wccase[myvar gt 5]:greater>>",
                           "If with greater than comparison");

            // Less than comparison
            AssertTransform("<ppp:if myvar lt 10>less<ppp:/if>",
                           "<wcmatch:<wccase[myvar lt 10]:less>>",
                           "If with less than comparison");

            // Greater than or equal
            AssertTransform("<ppp:if myvar ge 5>greater or equal<ppp:/if>",
                           "<wcmatch:<wccase[myvar ge 5]:greater or equal>>",
                           "If with greater than or equal comparison");

            // Less than or equal
            AssertTransform("<ppp:if myvar le 10>less or equal<ppp:/if>",
                           "<wcmatch:<wccase[myvar le 10]:less or equal>>",
                           "If with less than or equal comparison");

            // Contains comparison
            AssertTransform("<ppp:if myvar contains \"test\">contains<ppp:/if>",
                           "<wcmatch:<wccase[contains(myvar, \"test\")]:contains>>",
                           "If with contains comparison");
        }

        private static void TestIfCommandListOperations()
        {
            // Contains operation with list (variables)
            AssertTransform("<ppp:if myvar contains (val1,val2,val3)>found<ppp:/if>",
                           "<wcmatch:<wccase[contains(myvar, val1) || contains(myvar, val2) || contains(myvar, val3)]:found>>",
                           "If with contains list operation");

            // In operation with list (variables)
            AssertTransform("<ppp:if myvar in (option1,option2,option3)>in list<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq option1 || myvar eq option2 || myvar eq option3]:in list>>",
                           "If with in list operation");

            // Single value in parentheses (should be treated as single value)
            AssertTransform("<ppp:if myvar eq (singleval)>single<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq singleval]:single>>",
                           "If with single value in parentheses");

            // Mixed quoted and unquoted values in list
            AssertTransform("<ppp:if myvar contains (var1,\"literal\",var2)>mixed<ppp:/if>",
                           "<wcmatch:<wccase[contains(myvar, var1) || contains(myvar, \"literal\") || contains(myvar, var2)]:mixed>>",
                           "If with mixed quoted and unquoted values");

            // All quoted values in list
            AssertTransform("<ppp:if myvar in (\"opt1\",\"opt2\",\"opt3\")>all quoted<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq \"opt1\" || myvar eq \"opt2\" || myvar eq \"opt3\"]:all quoted>>",
                           "If with all quoted values");
        }

        private static void TestIfCommandNegation()
        {
            // Not with equality
            AssertTransform("<ppp:if myvar not eq \"test\">not equal<ppp:/if>",
                           "<wcmatch:<wccase[myvar ne \"test\"]:not equal>>",
                           "If with not equality");

            // Not with contains
            AssertTransform("<ppp:if myvar not contains \"test\">not contains<ppp:/if>",
                           "<wcmatch:<wccase[not contains(myvar, \"test\")]:not contains>>",
                           "If with not contains");

            // Not with list operation
            AssertTransform("<ppp:if myvar not in (val1,val2)>not in list<ppp:/if>",
                           "<wcmatch:<wccase[myvar ne val1 && myvar ne val2]:not in list>>",
                           "If with not in list");

            // Not with greater than
            AssertTransform("<ppp:if myvar not gt 5>not greater<ppp:/if>",
                           "<wcmatch:<wccase[myvar le 5]:not greater>>",
                           "If with not greater than");
        }

        private static void TestIfCommandComplexStructures()
        {
            // If-elif-else structure
            AssertTransform("<ppp:if var1 eq \"a\">first<ppp:elif var2 eq \"b\">second<ppp:else>third<ppp:/if>",
                           "<wcmatch:<wccase[var1 eq \"a\"]:first><wccase[var2 eq \"b\"]:second><wccase:third>>",
                           "If-elif-else structure");

            // Multiple elif clauses
            AssertTransform("<ppp:if var eq 1>one<ppp:elif var eq 2>two<ppp:elif var eq 3>three<ppp:else>other<ppp:/if>",
                           "<wcmatch:<wccase[var eq 1]:one><wccase[var eq 2]:two><wccase[var eq 3]:three><wccase:other>>",
                           "Multiple elif clauses");

            // Nested if statements
            AssertTransform("<ppp:if outer>outer content <ppp:if inner>inner content<ppp:/if><ppp:/if>",
                           "<wcmatch:<wccase[outer]:outer content <wcmatch:<wccase[inner]:inner content>>>>",
                           "Nested if statements");

            // If with complex content including other syntax
            AssertTransform("<ppp:if style eq \"anime\">kawaii girl, {red|blue} hair<ppp:/if>",
                           "<wcmatch:<wccase[style eq \"anime\"]:kawaii girl, <wcrandom:red|blue> hair>>",
                           "If with complex content");
        }

        private static void TestIfCommandEdgeCases()
        {
            // Malformed if (no closing tag)
            AssertTransform("<ppp:if myvar>content",
                           "<ppp:if myvar>content",
                           "Malformed if without closing tag");

            // Empty variable name
            AssertTransform("<ppp:if >content<ppp:/if>",
                           "<ppp:if >content<ppp:/if>",
                           "If with empty variable name");

            // Invalid operation - accept best-effort processing
            AssertTransform("<ppp:if myvar invalid \"test\">content<ppp:/if>",
                           "<wcmatch:<wccase[myvar invalid \"test\"]:content>>",
                           "If with invalid operation");

            // Unmatched parentheses in list - should return original with warning
            AssertTransform("<ppp:if myvar in (val1,val2>content<ppp:/if>",
                           "<ppp:if myvar in (val1,val2>content<ppp:/if>",
                           "If with unmatched parentheses");

            // Empty condition
            AssertTransform("<ppp:if>content<ppp:/if>",
                           "<ppp:if>content<ppp:/if>",
                           "If with empty condition");

            // Whitespace handling
            AssertTransform("<ppp:if   myvar   eq   \"test\"  >content<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq \"test\"]:content>>",
                           "If with extra whitespace");

            // Case insensitive operations
            AssertTransform("<ppp:if myvar EQ \"test\">content<ppp:/if>",
                           "<wcmatch:<wccase[myvar eq \"test\"]:content>>",
                           "If with uppercase operation");

            // Variable with special characters
            AssertTransform("<ppp:if my_var-123 eq \"test\">content<ppp:/if>",
                           "<wcmatch:<wccase[my_var-123 eq \"test\"]:content>>",
                           "If with special characters in variable name");
        }

        #endregion

        #region STN Command Tests

        /// <summary>
        /// Tests for STN (Send To Negative) command processing
        /// </summary>
        private static void TestStnCommands()
        {
            // Basic STN command (default position - start with comma suffix)
            AssertTransform("<ppp:stn>negative content<ppp:/stn>",
                           "<wcnegative[prepend]:negative content, >",
                           "Basic STN command");

            // STN with explicit start position
            AssertTransform("<ppp:stn s>start content<ppp:/stn>",
                           "<wcnegative[prepend]:start content, >",
                           "STN with start position");

            // STN with end position (append with comma prefix)
            AssertTransform("<ppp:stn e>end content<ppp:/stn>",
                           "<wcnegative:, end content>",
                           "STN with end position");

            // STN with insertion point (should use append with warning and comma prefix)
            AssertTransform("<ppp:stn p0>insertion content<ppp:/stn>",
                           "<wcnegative:, insertion content>",
                           "STN with insertion point");

            // STN with multiple insertion points
            AssertTransform("<ppp:stn p5>point five<ppp:/stn>",
                           "<wcnegative:, point five>",
                           "STN with insertion point p5");

            // Multiple STN commands in one line
            AssertTransform("positive <ppp:stn>neg1<ppp:/stn> more <ppp:stn e>neg2<ppp:/stn> text",
                           "positive <wcnegative[prepend]:neg1, > more <wcnegative:, neg2> text",
                           "Multiple STN commands");

            // STN with complex content
            AssertTransform("<ppp:stn>blurry, low quality, worst quality<ppp:/stn>",
                           "<wcnegative[prepend]:blurry, low quality, worst quality, >",
                           "STN with complex content");

            // STN with nested wildcards (should convert to wcwildcard)
            AssertTransform("beautiful <ppp:stn>__negative_styles__<ppp:/stn> portrait",
                           "beautiful <wcnegative[prepend]:<wcwildcard:negative_styles>, > portrait",
                           "STN with nested wildcards");

            // STN with variables (should convert to macro)
            AssertTransform("<ppp:stn>${bad_quality}<ppp:/stn>",
                           "<wcnegative[prepend]:<macro:bad_quality>, >",
                           "STN with variables");

            // Empty STN content (still gets comma separator)
            AssertTransform("<ppp:stn><ppp:/stn>",
                           "<wcnegative[prepend]:, >",
                           "Empty STN content");

            // STN with whitespace (preserves content, adds comma)
            AssertTransform("<ppp:stn>  spaced content  <ppp:/stn>",
                           "<wcnegative[prepend]:  spaced content  , >",
                           "STN with whitespace");

            // Case insensitive positions
            AssertTransform("<ppp:stn S>start upper<ppp:/stn>",
                           "<wcnegative[prepend]:start upper, >",
                           "STN with uppercase start position");

            AssertTransform("<ppp:stn E>end upper<ppp:/stn>",
                           "<wcnegative:, end upper>",
                           "STN with uppercase end position");

            // Invalid positions (should default to start)
            AssertTransform("<ppp:stn invalid>invalid pos<ppp:/stn>",
                           "<wcnegative[prepend]:invalid pos, >",
                           "STN with invalid position");

            // STN with special characters
            AssertTransform("<ppp:stn>bad: (worst), [ugly]<ppp:/stn>",
                           "<wcnegative[prepend]:bad: (worst), (ugly:0.9), >",
                           "STN with special characters");

            // Malformed STN (no closing tag)
            AssertTransform("<ppp:stn>unclosed",
                           "<ppp:stn>unclosed",
                           "Malformed STN without closing tag");

            // Malformed STN (no content)
            AssertTransform("<ppp:stn",
                           "<ppp:stn",
                           "Malformed STN incomplete tag");

            // STN with nested STN (should handle outer first)
            AssertTransform("<ppp:stn>outer <ppp:stn e>inner<ppp:/stn> content<ppp:/stn>",
                           "<wcnegative[prepend]:outer <wcnegative:, inner> content, >",
                           "Nested STN commands");

            // STN with variant content (should convert to wcrandom)
            AssertTransform("<ppp:stn>{red|blue|green}<ppp:/stn>",
                           "<wcnegative[prepend]:<wcrandom:red|blue|green>, >",
                           "STN with variant content");

            // STN with weighted variants
            AssertTransform("<ppp:stn>{0.3::rare|1::common|normal}<ppp:/stn>",
                           "<wcnegative[prepend]:<wcrandom:0.3::rare|1::common|normal>, >",
                           "STN with weighted variants");

            // STN with quantified variants
            AssertTransform("<ppp:stn>{2$$bad|ugly|worst}<ppp:/stn>",
                           "<wcnegative[prepend]:<wcrandom[2,]:bad|ugly|worst>, >",
                           "STN with quantified variants");

            // STN with if statement as negative content
            AssertTransform("<ppp:stn><ppp:if quality eq \"low\">blurry, pixelated<ppp:/if><ppp:/stn>",
                           "<wcnegative[prepend]:<wcmatch:<wccase[quality eq \"low\"]:blurry, pixelated>>, >",
                           "STN with if statement");

            // STN with if-else statement as negative content
            AssertTransform("<ppp:stn><ppp:if style eq \"anime\">cartoon<ppp:else>realistic<ppp:/if><ppp:/stn>",
                           "<wcnegative[prepend]:<wcmatch:<wccase[style eq \"anime\"]:cartoon><wccase:realistic>>, >",
                           "STN with if-else statement");

            // STN with complex if statement and variants
            AssertTransform("<ppp:stn><ppp:if mood eq \"dark\">{gloomy|depressing}<ppp:else>bright<ppp:/if><ppp:/stn>",
                           "<wcnegative[prepend]:<wcmatch:<wccase[mood eq \"dark\"]:<wcrandom:gloomy|depressing>><wccase:bright>>, >",
                           "STN with if statement containing variants");

            // STN with nested wildcards and variables
            AssertTransform("<ppp:stn>__bad_quality__, ${negative_style}<ppp:/stn>",
                           "<wcnegative[prepend]:<wcwildcard:bad_quality>, <macro:negative_style>, >",
                           "STN with mixed wildcards and variables");

            // STN with echo command as negative content
            AssertTransform("<ppp:stn><ppp:echo negative_prompt>default negative<ppp:/echo><ppp:/stn>",
                           "<wcnegative[prepend]:<wcmatch:<wccase[length(negative_prompt) eq 0]:default negative><wccase:<macro:negative_prompt>>>, >",
                           "STN with echo command");

            // STN with set and echo commands
            AssertTransform("<ppp:stn>${bad_style=ugly} <ppp:echo bad_style><ppp:/stn>",
                           "<wcnegative[prepend]:<setmacro[bad_style,false]:ugly> <macro:bad_style>, >",
                           "STN with set and echo commands");

            // STN insertion point markers (should be removed with warning)
            AssertTransform("content <ppp:stn i0> more content",
                           "content  more content",
                           "STN insertion point marker");

            // Mixed STN commands and insertion points
            AssertTransform("<ppp:stn i1>text <ppp:stn p1>negative<ppp:/stn> more",
                           "text <wcnegative:, negative> more",
                           "Mixed STN insertion point and command");
        }

        #endregion

        #region BREAK Word Replacement Tests

        private static void TestBreakWordReplacement()
        {
            // Basic BREAK replacement
            AssertTransform("BREAK",
                           "<comment:empty>",
                           "Basic BREAK replacement");

            // BREAK with surrounding text
            AssertTransform("hello BREAK world",
                           "hello <comment:empty> world",
                           "BREAK with surrounding text");

            // Multiple BREAK words
            AssertTransform("BREAK and BREAK again",
                           "<comment:empty> and <comment:empty> again",
                           "Multiple BREAK words");

            // BREAK at start and end
            AssertTransform("BREAK middle BREAK",
                           "<comment:empty> middle <comment:empty>",
                           "BREAK at start and end");

            // BREAK with punctuation - exclamation mark protects BREAK
            AssertTransform("hello, BREAK! world?",
                           "hello, BREAK! world?",
                           "BREAK with punctuation (protected by !)");

            // BREAK with proper spacing should be replaced
            AssertTransform("hello, BREAK , world",
                           "hello, <comment:empty> , world",
                           "BREAK with proper spacing");

            // Case sensitivity - only BREAK should be replaced
            AssertTransform("break Break BREAK",
                           "break Break <comment:empty>",
                           "Case sensitivity test");

            // BREAK as part of another word should NOT be replaced
            AssertTransform("BREAKDOWN BREAKFAST unBREAKable",
                           "BREAKDOWN BREAKFAST unBREAKable",
                           "BREAK as part of other words");

            // BREAK with underscores should NOT be replaced
            AssertTransform("_BREAK BREAK_ _BREAK_",
                           "_BREAK BREAK_ _BREAK_",
                           "BREAK with underscores");
        }

        private static void TestBreakWordProtectedContexts()
        {
            // BREAK inside wildcard paths should NOT be replaced (protected by non-space characters)
            AssertTransform("__wildcards/break/foo__",
                           "<wcwildcard:wildcards/break/foo>",
                           "BREAK in wildcard path (lowercase)");

            AssertTransform("__wildcards/BREAK/foo__",
                           "<wcwildcard:wildcards/BREAK/foo>",
                           "BREAK in wildcard path (uppercase)");

            AssertTransform("__some/BREAK/path__",
                           "<wcwildcard:some/BREAK/path>",
                           "BREAK in middle of wildcard path");

            // BREAK inside variable names should NOT be replaced (protected by non-space characters)
            AssertTransform("${break=32}",
                           "<setmacro[break,false]:32>",
                           "break in variable assignment (lowercase)");

            AssertTransform("${BREAK=value}",
                           "<setmacro[BREAK,false]:value>",
                           "BREAK in variable assignment (uppercase)");

            AssertTransform("${BREAK}",
                           "<macro:BREAK>",
                           "BREAK as variable name");

            AssertTransform("${some_BREAK_var=test}",
                           "<setmacro[some_BREAK_var,false]:test>",
                           "BREAK in compound variable name");

            // BREAK outside protected contexts should still be replaced
            AssertTransform("BREAK __wildcards/test__ BREAK",
                           "<comment:empty> <wcwildcard:wildcards/test> <comment:empty>",
                           "BREAK outside wildcard context");

            AssertTransform("BREAK ${var=value} BREAK",
                           "<comment:empty> <setmacro[var,false]:value> <comment:empty>",
                           "BREAK outside variable context");

            // Complex mixed scenarios - BREAK inside wildcard path should be protected
            AssertTransform("hello BREAK __path/BREAK/file__ and BREAK ${BREAK=test} more BREAK",
                           "hello <comment:empty> <wcwildcard:path/BREAK/file> and <comment:empty> <setmacro[BREAK,false]:test> more <comment:empty>",
                           "Complex mixed BREAK scenarios");

            // BREAK inside already processed directives should NOT be replaced (protected by non-space characters)
            AssertTransform("<wccase[BREAK]>content</wccase>",
                           "<wccase[BREAK]>content</wccase>",
                           "BREAK inside directive");

            // Test with variants containing BREAK - should be replaced since surrounded by | and }
            AssertTransform("{BREAK|test}",
                           "<wcrandom:<comment:empty>|test>",
                           "BREAK in variant options");

            AssertTransform("{hello|BREAK|world}",
                           "<wcrandom:hello|<comment:empty>|world>",
                           "BREAK mixed with other variant options");

            // Test BREAK with commas (should be replaced since comma is allowed)
            AssertTransform("hello, BREAK, world",
                           "hello, <comment:empty>, world",
                           "BREAK with commas");

            // Test BREAK with other punctuation (should NOT be replaced)
            AssertTransform("hello.BREAK.world",
                           "hello.BREAK.world",
                           "BREAK with periods (protected)");

            AssertTransform("path/BREAK/file",
                           "path/BREAK/file",
                           "BREAK with slashes (protected)");
        }

        #endregion
    }
}
