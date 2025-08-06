using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    using SwarmUI.Accounts;
    using SwarmUI.Text2Image;
    using SwarmUI.WebAPI;

    public class WildcardImporterExtension : Extension
    {
        private WildcardImporterAPI _api = null;

        private T2IRegisteredParam<bool> PromptCleanup;
        private T2IRegisteredParam<bool> PromptAutoBreak;

        public static PermInfoGroup WildcardImporterPermGroup = new("Wildcard Importer", "Permissions related to the Wildcard Importer Extension.");

        // RISKY because it can overwrite wildcards
        // RISKY because it can navigate out of Wildcards folder -- I didn't add code to prevent a name with ../ in it for example.
        public static PermInfo WildcardImporterCalls = Permissions.Register(new("wildcard_importer_calls", "Import Wildcards", "Allows this user to import wildcards via Wildcard Importer Extension.", PermissionDefault.POWERUSERS, WildcardImporterPermGroup, PermSafetyLevel.RISKY));

        public override void OnPreInit()
        {
            Logs.Debug("WildcardImporter Extension started.");
            ScriptFiles.Add("Assets/wildcard_importer.js");
            ScriptFiles.Add("Assets/dropzone-min.js");
            StyleSheetFiles.Add("Assets/wildcard_importer.css");
            StyleSheetFiles.Add("Assets/dropzone.css");
            OtherAssets.Add("Assets/dropzone-min.js.map");
            OtherAssets.Add("Assets/dropzone.css.map");
        }

        public override void OnInit()
        {
            var yamlParser = new YamlParser(this.FilePath);
            var processor = new WildcardProcessor(yamlParser);
            _api = new WildcardImporterAPI(processor);
            _api.Register();

            AddT2IParameters();
        }

        public void AddT2IParameters()
        {
            var paramGroup = new T2IParamGroup("Wildcard Importer Prompt Extensions", Toggles: true, Open: false, IsAdvanced: false, OrderPriority: 9);
            PromptCleanup = T2IParamTypes.Register<bool>(new(
                Name: "Cleanup Prompts",
                Description: "Cleanup the prompt before submitting it to the model:\n\nConverts 'girl1, ,,, \nabsurdres,detailed,(perfect eyes),realistic  ' to 'girl1, absurdres, detailed, (perfect eyes), realistic'\n - Replace newlines with space\n - Replace multiple spaces with a single space\n - Replace multiple commas with a single comma\n - Ensure there is a single space after a comma or closing parenthesis\n - Remove trailing commas and whitespace",
                Default: "false",
                Group: paramGroup,
                OrderPriority: 1
            ));
            PromptAutoBreak = T2IParamTypes.Register<bool>(new(
                Name: "AutoBreak",
                Description: "Automatically insert <break> tags in long prompts to keep each part <= 75 tokens.\nOptimized for booru tag prompting style, this will intelligently look for safe places to break your prompt where it will not split a prompt mid-tag.\n\nFree yourself from token counting.  Disable if not using CLIP.",
                Default: "false",
                Group: paramGroup,
                OrderPriority: 1
            ));

            T2IParamInput.LateSpecialParameterHandlers.Add(userInput => 
                {
                    // if PromptCleanup is true, run the pos and neg prompts through Clean
                    if (userInput.InternalSet.Get(PromptCleanup))
                    {
                        var posPrompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
                        if (posPrompt != null)
                        {
                            userInput.InternalSet.Set(T2IParamTypes.Prompt, Clean(posPrompt));
                        }

                        var negPrompt = userInput.InternalSet.Get(T2IParamTypes.NegativePrompt);
                        if (negPrompt != null)
                        {
                            userInput.InternalSet.Set(T2IParamTypes.NegativePrompt, Clean(negPrompt));
                        }
                    }
                    // if PromptAutoBreak is true, run the pos and neg prompts through AutoBreak
                    if (userInput.InternalSet.Get(PromptAutoBreak))
                    {
                        var posPrompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
                        if (posPrompt != null)
                        {
                            userInput.InternalSet.Set(T2IParamTypes.Prompt, AutoBreak(posPrompt).Result);
                        }

                        var negPrompt = userInput.InternalSet.Get(T2IParamTypes.NegativePrompt);
                        if (negPrompt != null)
                        {
                            userInput.InternalSet.Set(T2IParamTypes.NegativePrompt, AutoBreak(negPrompt).Result);
                        }
                    }
                });
        }

        /// <summary>
        /// Synchronous version of ForeachPromptSection that applies a transformation function to text segments between tags.
        /// </summary>
        /// <param name="prompt">The input prompt string</param>
        /// <param name="func">Function to transform text segments between tags</param>
        /// <returns>The reassembled prompt with transformed text segments and original tags</returns>
        private string ForeachPromptSection(string prompt, Func<string, string> func)
        {
            if (string.IsNullOrEmpty(prompt))
                return prompt;

            var segments = ParsePromptSegments(prompt);
            var result = new System.Text.StringBuilder();

            foreach (var segment in segments)
            {
                if (segment.IsTag)
                {
                    result.Append(segment.Content);
                }
                else
                {
                    result.Append(func(segment.Content));
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Async version of ForeachPromptSection that applies an async transformation function to text segments between tags.
        /// </summary>
        /// <param name="prompt">The input prompt string</param>
        /// <param name="func">Async function to transform text segments between tags</param>
        /// <returns>The reassembled prompt with transformed text segments and original tags</returns>
        private async Task<string> ForeachPromptSection(string prompt, Func<string, Task<string>> func)
        {
            if (string.IsNullOrEmpty(prompt))
                return prompt;

            var segments = ParsePromptSegments(prompt);
            var result = new System.Text.StringBuilder();

            foreach (var segment in segments)
            {
                if (segment.IsTag)
                {
                    result.Append(segment.Content);
                }
                else
                {
                    string transformed = await func(segment.Content);
                    result.Append(transformed);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Represents a segment of a prompt that is either a tag or text content.
        /// </summary>
        private record PromptSegment(string Content, bool IsTag);

        /// <summary>
        /// Parses a prompt into segments, separating tags from text content.
        /// Tags are enclosed in &lt; and &gt; and can be nested.
        /// </summary>
        /// <param name="prompt">The input prompt string</param>
        /// <returns>List of prompt segments</returns>
        private List<PromptSegment> ParsePromptSegments(string prompt)
        {
            var segments = new List<PromptSegment>();
            int i = 0;
            int textStart = 0;

            while (i < prompt.Length)
            {
                if (prompt[i] == '<')
                {
                    // Found start of a tag, process any text before it
                    if (i > textStart)
                    {
                        string textSegment = prompt.Substring(textStart, i - textStart);
                        segments.Add(new PromptSegment(textSegment, false));
                    }

                    // Find the end of the tag, handling nested tags
                    var tagInfo = FindCompleteTag(prompt, i);
                    
                    if (tagInfo.IsComplete)
                    {
                        segments.Add(new PromptSegment(tagInfo.Content, true));
                        i = tagInfo.EndIndex;
                        textStart = i;
                    }
                    else
                    {
                        // Unclosed tag - treat the '<' as regular text
                        string textSegment = prompt.Substring(i, 1);
                        segments.Add(new PromptSegment(textSegment, false));
                        textStart = i + 1;
                        i = i + 1;
                    }
                }
                else
                {
                    i++;
                }
            }

            // Process any remaining text after the last tag
            if (textStart < prompt.Length)
            {
                string textSegment = prompt.Substring(textStart);
                segments.Add(new PromptSegment(textSegment, false));
            }

            return segments;
        }

        /// <summary>
        /// Represents information about a tag found in the prompt.
        /// </summary>
        private record TagInfo(string Content, int EndIndex, bool IsComplete);

        /// <summary>
        /// Finds a complete tag starting at the specified position, handling nested tags.
        /// </summary>
        /// <param name="prompt">The prompt string</param>
        /// <param name="startIndex">The starting index of the tag (position of '&lt;')</param>
        /// <returns>Information about the found tag</returns>
        private TagInfo FindCompleteTag(string prompt, int startIndex)
        {
            int tagStart = startIndex;
            int bracketCount = 1;
            int i = startIndex + 1; // Move past the opening '<'

            while (i < prompt.Length && bracketCount > 0)
            {
                if (prompt[i] == '<')
                {
                    bracketCount++;
                }
                else if (prompt[i] == '>')
                {
                    bracketCount--;
                }
                i++;
            }

            if (bracketCount == 0)
            {
                string tag = prompt.Substring(tagStart, i - tagStart);
                return new TagInfo(tag, i, true);
            }
            else
            {
                return new TagInfo("", startIndex + 1, false);
            }
        }

        private string Clean(String prompt)
        {
            return ForeachPromptSection(prompt, CleanTextSegment);
        }

        private string CleanTextSegment(string textSegment)
        {
            // replace newlines with spaces
            textSegment = textSegment.Replace("\n", " ");
            
            // Ensure every "," or ")" has a space after it
            textSegment = System.Text.RegularExpressions.Regex.Replace(textSegment, @"([,)])(?!\s)", "$1 ");

            // Replace 2+ whitespace with single space
            textSegment = System.Text.RegularExpressions.Regex.Replace(textSegment, @"\s{2,}", " ");

            // Replace 2+ ", " with single ", "
            textSegment = System.Text.RegularExpressions.Regex.Replace(textSegment, @"(\s*,\s*)+", ", ");
            
            // Remove leading and trailing ", " and " "
            textSegment = textSegment.Trim(' ', ',');

            return textSegment;
        }
        
        /// <summary>
        /// Processes a prompt by automatically breaking up text segments that exceed 75 tokens.
        /// Uses async token counting for better performance.
        /// </summary>
        /// <param name="prompt">The prompt to process</param>
        /// <returns>The processed prompt with auto-break tags</returns>
        private async Task<string> AutoBreak(String prompt)
        {
            return await ForeachPromptSection(prompt, segment => AutoBreakTextSegment(segment));
        }

        /// <summary>
        /// Counts the tokens in a text segment using the SwarmUI CountTokens API.
        /// </summary>
        /// <param name="text">The text to count tokens for</param>
        /// <returns>The number of tokens in the text</returns>
        private async Task<int> CountTokens(string text)
        {
            try
            {
                // Replace [option1|option2|option3] patterns with their longest option for token counting
                string processedText = ProcessBracketPatternsForTokenCounting(text);
                var result = await SwarmUI.WebAPI.UtilAPI.CountTokens(processedText, skipPromptSyntax: true);
                return result["count"]?.ToObject<int>() ?? (text.Length / 4);
            }
            catch
            {
                // Fallback: rough estimation (average 4 characters per token)
                return Math.Max(1, text.Length / 4);
            }
        }

        /// <summary>
        /// Processes [option1|option2|option3] patterns by replacing them with the longest option for token counting.
        /// </summary>
        /// <param name="text">The text to process</param>
        /// <returns>Text with bracket patterns replaced by their longest options</returns>
        private string ProcessBracketPatternsForTokenCounting(string text)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '[' && !IsEscaped(text, i))
                {
                    // Find the matching closing bracket
                    int bracketEnd = FindMatchingClosingBracket(text, i);
                    if (bracketEnd != -1)
                    {
                        // Extract the content inside brackets
                        string bracketContent = text.Substring(i + 1, bracketEnd - i - 1);
                        
                        // Split on unescaped | and find the longest option
                        string longestOption = GetLongestOptionFromBracketContent(bracketContent);
                        
                        result.Append(longestOption);
                        i = bracketEnd + 1;
                    }
                    else
                    {
                        // No matching bracket found, treat as regular text
                        result.Append(text[i]);
                        i++;
                    }
                }
                else
                {
                    result.Append(text[i]);
                    i++;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Finds the matching closing bracket for an opening bracket, handling escaped characters.
        /// </summary>
        /// <param name="text">The text to search in</param>
        /// <param name="openBracketPos">The position of the opening bracket</param>
        /// <returns>The position of the matching closing bracket, or -1 if not found</returns>
        private int FindMatchingClosingBracket(string text, int openBracketPos)
        {
            for (int i = openBracketPos + 1; i < text.Length; i++)
            {
                if (text[i] == ']' && !IsEscaped(text, i))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Splits bracket content on unescaped | or : characters and returns the longest option.
        /// For colon-separated patterns like [a:b:3.2], ignores the final option (3.2).
        /// For pipe-separated patterns like [a|b|c], considers all options.
        /// </summary>
        /// <param name="bracketContent">The content inside the brackets</param>
        /// <returns>The longest option from the bracket content</returns>
        private string GetLongestOptionFromBracketContent(string bracketContent)
        {
            var options = new List<string>();
            var currentOption = new System.Text.StringBuilder();
            char separator = '\0'; // Track which separator we're using
            
            // First pass: determine separator and split options
            for (int i = 0; i < bracketContent.Length; i++)
            {
                char ch = bracketContent[i];
                
                if ((ch == '|' || ch == ':') && !IsEscaped(bracketContent, i))
                {
                    // Set separator on first encounter
                    if (separator == '\0')
                    {
                        separator = ch;
                    }
                    
                    // Only split if this matches our established separator
                    if (ch == separator)
                    {
                        options.Add(UnescapeString(currentOption.ToString()));
                        currentOption.Clear();
                    }
                    else
                    {
                        // Different separator, treat as regular character
                        currentOption.Append(ch);
                    }
                }
                else
                {
                    currentOption.Append(ch);
                }
            }

            // Add the last option
            if (currentOption.Length > 0)
            {
                options.Add(UnescapeString(currentOption.ToString()));
            }

            // For colon-separated patterns, ignore the final option (it's usually a weight/parameter)
            if (separator == ':' && options.Count > 1)
            {
                options = options.Take(options.Count - 1).ToList();
            }

            // Return the longest option, or empty string if no options
            return options.Count > 0 ? options.OrderByDescending(opt => opt.Length).First() : "";
        }

        /// <summary>
        /// Removes escape characters from a string (unescapes \|, \:, and \]).
        /// </summary>
        /// <param name="text">The text to unescape</param>
        /// <returns>The unescaped text</returns>
        private string UnescapeString(string text)
        {
            return text.Replace("\\|", "|").Replace("\\:", ":").Replace("\\]", "]");
        }

        /// <summary>
        /// Checks if a position is inside a [option1|option2|option3] pattern.
        /// </summary>
        /// <param name="text">The text to check</param>
        /// <param name="pos">The position to check</param>
        /// <returns>True if the position is inside a bracket pattern</returns>
        private bool IsInsideBracketPattern(string text, int pos)
        {
            // Look backwards for an unescaped opening bracket
            for (int i = pos - 1; i >= 0; i--)
            {
                if (text[i] == '[' && !IsEscaped(text, i))
                {
                    // Found opening bracket, now look for closing bracket after our position
                    int closingBracket = FindMatchingClosingBracket(text, i);
                    return closingBracket > pos;
                }
                else if (text[i] == ']' && !IsEscaped(text, i))
                {
                    // Found closing bracket before opening bracket, we're not inside a pattern
                    break;
                }
            }
            return false;
        }

        /// <summary>
        /// Automatically breaks a text segment into smaller pieces if it exceeds 75 tokens.
        /// Splits on commas, parentheses, or whitespace while preserving proper formatting.
        /// </summary>
        /// <param name="textSegment">The text segment to potentially break up</param>
        /// <returns>The text segment with &lt;break&gt; tags inserted if needed</returns>
        private async Task<string> AutoBreakTextSegment(string textSegment)
        {
            if (string.IsNullOrWhiteSpace(textSegment))
                return textSegment;

            var segments = new List<string>();
            string remainingText = textSegment;

            while (await CountTokens(remainingText) > 75)
            {
                string splitPart = await FindBestSplitPoint(remainingText);
                if (splitPart == null)
                {
                    // No suitable split point found, keep remaining text as-is
                    segments.Add(remainingText);
                    remainingText = "";
                    break;
                }

                segments.Add(splitPart.TrimEnd(' ', ','));
                
                // Remove the split part from remaining text and clean up
                remainingText = remainingText.Substring(splitPart.Length).TrimStart(' ', ',');
            }

            if (!string.IsNullOrWhiteSpace(remainingText))
            {
                segments.Add(remainingText);
            }

            return string.Join("<break>", segments);
        }

        /// <summary>
        /// Finds the best point to split a text segment to keep it under 75 tokens.
        /// Prioritizes commas, then parentheses, then whitespace.
        /// </summary>
        /// <param name="text">The text to find a split point in</param>
        /// <returns>The portion of text up to the split point, or null if no suitable split found</returns>
        private async Task<string> FindBestSplitPoint(string text)
        {
            // Start from the end and work backwards to avoid bracket pattern edge cases
            // We'll search from the end down to a reasonable minimum (10 tokens worth)
            int startPos = text.Length - 1;
            int minPos = 40; // ~10 tokens worth of characters - reasonable minimum
            
            string fallbackSpaceSplit = null; // Remember the best space split we find
            
            for (int pos = startPos; pos > minPos; pos--)
            {
                char ch = text[pos];
                
                // Check for comma - split AFTER comma (highest priority)
                if (ch == ',' && !IsEscaped(text, pos) && !IsInsideBracketPattern(text, pos))
                {
                    string splitText = text.Substring(0, pos + 1);
                    if (await CountTokens(splitText.TrimEnd(' ', ',')) <= 75)
                    {
                        return splitText; // Immediately return - comma has highest priority
                    }
                }
                
                // Check for closing parenthesis - split AFTER ')' (high priority)
                if (ch == ')' && !IsEscaped(text, pos) && !IsInsideBracketPattern(text, pos))
                {
                    string splitText = text.Substring(0, pos + 1);
                    if (await CountTokens(splitText) <= 75)
                    {
                        return splitText; // Immediately return - parenthesis has high priority
                    }
                }
                
                // Check for opening parenthesis - split BEFORE '(' (high priority)
                if (ch == '(' && !IsEscaped(text, pos) && !IsInsideBracketPattern(text, pos))
                {
                    string splitText = text.Substring(0, pos).TrimEnd(' ');
                    if (await CountTokens(splitText) <= 75)
                    {
                        return splitText; // Immediately return - parenthesis has high priority
                    }
                }
                
                // Check for whitespace - remember as fallback but keep searching for punctuation
                if (ch == ' ' && fallbackSpaceSplit == null && !IsInsideBracketPattern(text, pos))
                {
                    string splitText = text.Substring(0, pos);
                    if (await CountTokens(splitText) <= 75)
                    {
                        fallbackSpaceSplit = splitText; // Remember this but keep searching for better options
                    }
                }
            }

            // If we didn't find any punctuation to split on, use the space split we remembered
            return fallbackSpaceSplit;
        }

        /// <summary>
        /// Checks if a character at the given position is escaped by backslashes.
        /// </summary>
        /// <param name="text">The text to check</param>
        /// <param name="pos">The position of the character</param>
        /// <returns>True if the character is escaped</returns>
        private bool IsEscaped(string text, int pos)
        {
            int backslashCount = 0;
            for (int i = pos - 1; i >= 0 && text[i] == '\\'; i--)
            {
                backslashCount++;
            }
            // If odd number of backslashes, the character is escaped
            return backslashCount % 2 == 1;
        }
    }
}