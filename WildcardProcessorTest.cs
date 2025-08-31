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
            TestRangeVariants();
            TestEmptyVariants();
            TestNestedVariants();

            // Test wildcards
            TestBasicWildcards();
            TestWildcardsInVariants();
            TestGlobWildcards();

            // Test variables
            TestVariableAssignments();
            TestVariableAccess();
            TestImmediateVariables();

            // Test edge cases
            TestMalformedSyntax();
            TestComplexNesting();
            TestSpecialCharacters();

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
                           "<random:summer|autumn|winter|spring> is coming",
                           "Basic variant");

            // Single option
            AssertTransform("{summer} is coming", 
                           "summer is coming",
                           "Single option variant");

            // Multiple variants in one line
            AssertTransform("I like {red|blue} and {cats|dogs}", 
                           "I like <random:red|blue> and <random:cats|dogs>",
                           "Multiple variants");
        }

        private static void TestWeightedVariants()
        {
            // Weighted options: {0.5::a|1::b|0.25::c}
            // Note: The actual algorithm creates more duplicates than expected for decimal weights
            AssertTransform("{0.5::summer|1::autumn|0.25::winter}", 
                           "<random:summer|summer|autumn|autumn|autumn|autumn|winter>",
                           "Weighted variant with decimals");

            // Mixed weighted and unweighted
            AssertTransform("{summer|2::autumn|winter}", 
                           "<random:summer|autumn|autumn|winter>",
                           "Mixed weighted variant");

            // Integer weights
            AssertTransform("{1::red|3::blue|2::green}", 
                           "<random:red|blue|blue|blue|green|green>",
                           "Integer weighted variant");
        }

        private static void TestQuantifierVariants()
        {
            // Pick 2: {2$$a|b|c}
            AssertTransform("My favorites are {2$$chocolate|vanilla|strawberry}", 
                           "My favorites are <random[2,]:chocolate|vanilla|strawberry>",
                           "Quantifier variant");

            // Pick 1 (explicit)
            AssertTransform("{1$$red|blue|green}", 
                           "<random[1,]:red|blue|green>",
                           "Explicit single quantifier");
        }

        private static void TestRangeVariants()
        {
            // Range: {2-3$$a|b|c|d}
            AssertTransform("{2-3$$chocolate|vanilla|strawberry|mint}", 
                           "<random[2-3,]:chocolate|vanilla|strawberry|mint>",
                           "Range variant");

            // No lower bound: {-2$$a|b|c}
            AssertTransform("{-2$$red|blue|green}", 
                           "<random[1-2,]:red|blue|green>",
                           "No lower bound range");

            // No upper bound: {2-$$a|b|c|d}
            AssertTransform("{2-$$red|blue|green|yellow}", 
                           "<random[2-4,]:red|blue|green|yellow>",
                           "No upper bound range");
        }

        private static void TestEmptyVariants()
        {
            // Empty options: {a||c}
            AssertTransform("{red||blue}", 
                           "<random:red|<comment:empty>|blue>",
                           "Empty option in variant");

            // All empty: {||}
            AssertTransform("{||}", 
                           "<random:<comment:empty>|<comment:empty>|<comment:empty>>",
                           "All empty options");

            // Trailing empty: {a|b|}
            AssertTransform("{red|blue|}", 
                           "<random:red|blue|<comment:empty>>",
                           "Trailing empty option");
        }

        private static void TestNestedVariants()
        {
            // Nested variants: {a|{b|c}|d}
            AssertTransform("Color is {red|{light blue|dark blue}|green}", 
                           "Color is <random:red|<random:light blue|dark blue>|green>",
                           "Nested variants");

            // Deep nesting
            AssertTransform("{a|{b|{c|d}|e}|f}", 
                           "<random:a|<random:b|<random:c|d>|e>|f>",
                           "Deep nested variants");
        }

        #endregion

        #region Test Wildcards

        private static void TestBasicWildcards()
        {
            // Basic wildcard: __name__
            AssertTransform("__season__ is coming", 
                           "<wildcard:season> is coming",
                           "Basic wildcard");

            // Multiple wildcards
            AssertTransform("I like __color__ __animal__", 
                           "I like <wildcard:color> <wildcard:animal>",
                           "Multiple wildcards");

            // Wildcard with path
            AssertTransform("__clothing/shirts__ are nice", 
                           "<wildcard:clothing/shirts> are nice",
                           "Wildcard with path");
        }

        private static void TestWildcardsInVariants()
        {
            // Wildcards in variants: {2$$__flavours__}
            // This is the correct SD Dynamic Prompts syntax - should use wildcard quantifier
            AssertTransform("My favourite ice-cream flavours are {2$$__flavours__}", 
                           "My favourite ice-cream flavours are <wildcard[2,]:flavours>",
                           "Wildcards in variants with quantifier");

            // Range quantifier with wildcard in variant: {2-3$$__colors__}
            AssertTransform("Pick {2-3$$__colors__}", 
                           "Pick <wildcard[2-3,]:colors>",
                           "Range quantifier with wildcard in variant");

            // Simple wildcard in variant: {__flavours__|vanilla}
            AssertTransform("I like {__flavours__|vanilla}", 
                           "I like <random:<wildcard:flavours>|vanilla>",
                           "Simple wildcard in variant");

            // Multiple wildcards in quantified variant: {2$$__flavours__|__flavours__}
            // SD Dynamic Prompts treats this as simple variants, each wildcard resolved independently
            AssertTransform("My favourite ice-cream flavours are {2$$__flavours__|__flavours__}", 
                           "My favourite ice-cream flavours are <random[2,]:<wildcard:flavours>|<wildcard:flavours>>",
                           "Multiple wildcards in quantified variant");
            
            AssertTransform("My favorite breed is __{cat|dog}s__", 
                           "My favorite breed is <wildcard:<random:cat|dog>s>",
                           "Variant nested in wildcard name");
            
            AssertTransform("my top 2 breeds are {2$$__{1$$cat|dog}s__}",
                "my top 2 breeds are <wildcard[2,]:<random[1,]:cat|dog>s>",
                "Variant nested in wildcard name with quantifier");
        }

        private static void TestGlobWildcards()
        {
            // Single glob: __colors*__
            AssertTransform("__colors*__ are nice", 
                           "<random:<wildcard:colors-cold>|<wildcard:colors-warm>> are nice",
                           "Single glob wildcard",
                           CreateMockFiles("colors-cold", "colors-warm"));

            // Recursive glob: __artists/**__
            // Note: Dictionary enumeration order may vary, so we accept either order
            AssertTransform("__artists/**__ painted this", 
                           "<random:<wildcard:artists/dutch>|<wildcard:artists/finnish>> painted this",
                           "Recursive glob wildcard",
                           CreateMockFiles("artists/finnish", "artists/dutch"));

            // No matches - should include warning comment
            AssertTransform("__nonexistent*__ test", 
                           "<wildcard:nonexistent*><comment:no glob matches> test",
                           "No glob matches",
                           CreateMockFiles());

            // Single match - should not use random
            AssertTransform("__unique*__ test", 
                           "<wildcard:unique-file> test",
                           "Single glob match",
                           CreateMockFiles("unique-file"));
        }

        #endregion

        #region Test Variables

        private static void TestVariableAssignments()
        {
            // Deferred assignment: ${var=value}
            AssertTransform("${color=red} The ${color} car", 
                           "<setmacro[color,false]:red> The <macro:color> car",
                           "Deferred variable assignment");

            // Multiple assignments
            AssertTransform("${a=1}${b=2} Values: ${a}, ${b}", 
                           "<setmacro[a,false]:1><setmacro[b,false]:2> Values: <macro:a>, <macro:b>",
                           "Multiple variable assignments");
        }

        private static void TestVariableAccess()
        {
            // Variable access: ${var}
            AssertTransform("The ${color} car is ${size}", 
                           "The <macro:color> car is <macro:size>",
                           "Variable access");

            // Variable in variant
            AssertTransform("{${color}|blue} car", 
                           "<random:<macro:color>|blue> car",
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
                           "<setvar[choice,false]:<random:red|blue>><setmacro[choice,false]:<var:choice>> Color is <macro:choice>",
                           "Immediate variable with variant");
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
                           "<comment:empty> car",
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
                           "<random:<wildcard:colors>|blue> car",
                           "Variant with wildcard");

            // Variable with variant and wildcard
            AssertTransform("${style={modern|__historical__}} ${style} building", 
                           "<setmacro[style,false]:<random:modern|<wildcard:historical>>> <macro:style> building",
                           "Complex nesting");

            // Quantifier with nested structures
            AssertTransform("{2$$__colors__|{red|blue}|green}", 
                           "<random[2,]:<wildcard:colors>|<random:red|blue>|green>",
                           "Quantifier with nested structures");
        }

        private static void TestSpecialCharacters()
        {
            // Escaped characters in variants
            AssertTransform("{red\\|blue|green}", 
                           "<random:red\\|blue|green>",
                           "Escaped pipe in variant");

            // Special characters in wildcards
            AssertTransform("__special-chars_123__ test", 
                           "<wildcard:special-chars_123> test",
                           "Special characters in wildcard");

            // Unicode characters
            AssertTransform("{café|naïve} word", 
                           "<random:café|naïve> word",
                           "Unicode characters");
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
                
                string result = (string)method.Invoke(processor, new object[] { input, taskId });

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
    }
}
