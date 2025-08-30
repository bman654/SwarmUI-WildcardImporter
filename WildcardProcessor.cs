using System.IO;
using System.Text.RegularExpressions;
using System.IO.Compression;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    public class WildcardProcessor
    {
        private readonly YamlParser _yamlParser;
        
        // TODO: Allow setting the destination folder via API. Users might want to set it to their own custom folder.
        public readonly string destinationFolder = Utilities.CombinePathWithAbsolute(Program.ServerSettings.Paths.DataPath, Program.ServerSettings.Paths.WildcardsFolder);
        private readonly ConcurrentDictionary<string, ProcessingTask> _tasks = new();
        private readonly ConcurrentBag<ProcessingHistoryItem> _history = new();

        public WildcardProcessor(YamlParser yamlParser)
        {
            _yamlParser = yamlParser;
            // Directory.CreateDirectory(destinationFolder);
            Logs.Info($"WildcardProcessor initialized with destination folder: {destinationFolder}");
        }

        public async Task<string> ProcessFiles(List<FileData> files, string prefix)
        {
            string taskId = Guid.NewGuid().ToString();
            var task = new ProcessingTask { Id = taskId, InFiles = files.Count, Prefix = String.IsNullOrWhiteSpace(prefix) ? "" : $"{prefix.Trim()}/" };
            _tasks[taskId] = task;
            Logs.Info($"Started processing task: {taskId} with prefix '{task.Prefix}' for {files.Count} files: {string.Join(", ", files.Select(f => f.FilePath))}");

            _ = Task.Run(async () =>
            {
                try
                {
                    // First pass: Collect all files in memory
                    foreach (var file in files)
                    {
                        if (file.Base64Content == null)
                        {
                            if (!File.Exists(file.FilePath))
                            {
                                task.Errors.Add($"File not found: {file.FilePath}");
                                return;
                            }

                            string fileName = Path.GetFileName(file.FilePath);
                            try
                            {
                                await CollectFile(taskId, fileName, file.FilePath);
                            }
                            catch (Exception ex)
                            {
                                task.Errors.Add($"Error collecting {file.FilePath}: {ex.Message}");
                            }
                        }
                        else
                        {
                            string tempPath = Path.GetTempFileName();
                            try
                            {
                                byte[] fileBytes = Convert.FromBase64String(file.Base64Content);
                                await File.WriteAllBytesAsync(tempPath, fileBytes);
                                string fileName = Path.GetFileName(file.FilePath);

                                await CollectFile(taskId, fileName, tempPath);
                            }
                            catch (FormatException fe)
                            {
                                Logs.Error($"Invalid base64 content for file {file.FilePath}: {fe}");
                                task.Errors.Add($"Invalid base64 content for file {file.FilePath}: {fe.Message}");
                            }
                            catch (Exception ex)
                            {
                                Logs.Error($"Error collecting {file.FilePath}: {ex}");
                                task.Errors.Add($"Error collecting {file.FilePath}: {ex.Message}");
                            }
                            finally
                            {
                                if (File.Exists(tempPath))
                                {
                                    File.Delete(tempPath);
                                }
                            }
                        }

                        task.InFilesProcessed++;
                    }

                    // Second pass: Process wildcard references and write files
                    await ProcessCollectedFiles(taskId);

                    task.InMemoryFiles.Clear(); // free up some memory
                    
                    if (task.Errors.Count == 0)
                    {
                        task.Status = ProcessingStatusEnum.Completed;
                        _history.Add(new ProcessingHistoryItem {
                            TaskId = taskId,
                            Timestamp = DateTime.UtcNow,
                            Name = String.IsNullOrWhiteSpace(task.Prefix) ? "Wildcard" : task.Prefix.TrimEnd('/'),
                            Description = $"Read {task.InFiles}, wrote {task.OutFilesProcessed} files",
                            Success = true,
                            Warnings = task.Warnings,
                        });
                    }
                    else
                    {
                        task.Status = ProcessingStatusEnum.Failed;
                        _history.Add(new ProcessingHistoryItem {
                            TaskId = taskId,
                            Timestamp = DateTime.UtcNow,
                            Name = String.IsNullOrWhiteSpace(task.Prefix) ? "Wildcard" : task.Prefix.TrimEnd('/'),
                            Description = string.Join("\n", task.Errors),
                            Success = false,
                            Warnings = task.Warnings,
                        });
                    }
                }
                catch (Exception ex)
                {
                    task.Status = ProcessingStatusEnum.Failed;
                    task.Errors.Add($"Error processing files: {ex.Message}");
                    Logs.Error($"Error processing files: {ex.ReadableString()}");
                    task.InMemoryFiles.Clear(); // free up some memory
                    _history.Add(new ProcessingHistoryItem {
                        TaskId = taskId,
                        Timestamp = DateTime.UtcNow,
                        Name = String.IsNullOrWhiteSpace(task.Prefix) ? "Wildcard" : task.Prefix.TrimEnd('/'),
                        Description = string.Join("\n", task.Errors),
                        Success = false,
                        Warnings = task.Warnings,
                    });
                }
            });

            return taskId;
        }

        private async Task CollectFile(string taskId, string fileName, string filePath)
        {
            var task = _tasks[taskId];
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await CollectZipFile(taskId, filePath);
            }
            else if (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                string content = await File.ReadAllTextAsync(filePath);
                CollectYamlFile(taskId, content, fileName);
            }
            else if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                string content = await File.ReadAllTextAsync(filePath);
                CollectTextContent(taskId, content, fileName);
            }
        }

        private async Task CollectZipFile(string taskId, string zipPath)
        {
            Logs.Debug($"Collecting ZIP file contents: {zipPath}");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    {
                        await using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        string content = await reader.ReadToEndAsync();
                        CollectYamlFile(taskId, content, entry.FullName);
                    }
                    else if (entry.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        await using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        string content = await reader.ReadToEndAsync();
                        CollectTextContent(taskId, content, entry.FullName);
                    }
                }
            }
            Logs.Debug($"ZIP file contents collected: {zipPath}");
        }

        private void CollectYamlFile(string taskId, string yamlContent, string yamlPath)
        {
            Logs.Debug($"Collecting YAML file contents: {yamlPath}");
            var parsedYaml = _yamlParser.Parse(yamlContent);
            Logs.Debug($"parsedYaml: {parsedYaml.Count} entries");

            foreach (var topLevelKvp in parsedYaml)
            {
                string topLevelKey = topLevelKvp.Key;
                var topLevelValue = topLevelKvp.Value;
                Logs.Debug($"topLevelKey: {topLevelKey}, topLevelValue: {topLevelValue}");
                CollectYamlContent(taskId, topLevelKey, topLevelValue);
            }
            Logs.Debug($"YAML file contents collected: {yamlPath}");
        }

        private void CollectYamlContent(string taskId, string currentPath, object currentValue)
        {
            Logs.Debug($"Collecting YAML content: {currentPath}");
            var task = _tasks[taskId];
            
            // Handle Dictionary<string, object>
            if (currentValue == null)
            {
                task.AddWarning($"Empty yaml object at path: {currentPath}");
            }
            else if (currentValue is Dictionary<string, object> stringKeyMap)
            {
                foreach (var kvp in stringKeyMap)
                {
                    string key = kvp.Key;
                    var value = kvp.Value;
                    // Build the nested path by combining current path with key
                    string nestedPath = $"{currentPath}/{key}";
                    CollectYamlContent(taskId, nestedPath, value);
                }
            }
            // Handle Dictionary<object, object> by converting keys to strings
            else if (currentValue is Dictionary<object, object> objectKeyMap)
            {
                foreach (var kvp in objectKeyMap)
                {
                    // Convert key to string - YAML keys should always be convertible to strings
                    string key = kvp.Key.ToString();
                    var value = kvp.Value;
                    // Build the nested path by combining current path with key
                    string nestedPath = $"{currentPath}/{key}";
                    CollectYamlContent(taskId, nestedPath, value);
                }
            }
            else if (currentValue is List<object> currentList)
            {
                if (currentList.Count == 1 && currentList[0] is string singleItem)
                {
                    // Store in memory with path structure
                    task.InMemoryFiles.TryAdd(currentPath, new List<string> { singleItem });
                }
                else
                {
                    List<string> stringList = new List<string>();
                    foreach (var item in currentList)
                    {
                        if (item is string stringItem)
                        {
                            stringList.Add(stringItem);
                        }
                        else
                        {
                            // For non-string items in the list, we recursively process them with an indexed path
                            int index = currentList.IndexOf(item);
                            string indexedPath = $"{currentPath}/{index}";
                            CollectYamlContent(taskId, indexedPath, item);
                        }
                    }

                    if (stringList.Count > 0)
                    {
                        task.InMemoryFiles.TryAdd(currentPath, stringList);
                    }
                }
            }
            else if (currentValue is string stringValue)
            {
                // Store in memory with path structure - now using the full path
                task.InMemoryFiles.TryAdd(currentPath, new List<string> { stringValue });
            }
            else 
            {
                task.AddWarning($"Unknown YAML value type: {currentValue.GetType()}; path: {currentPath}; value: {currentValue}");
            }
        }

        private void CollectTextContent(string taskId, string content, string fileName)
        {
            var task = _tasks[taskId];
            Logs.Debug($"Collecting text file contents: {fileName}");
            var lines = content.Split('\n');
            
            // Store in memory with just the filename
            task.InMemoryFiles.TryAdd(fileName, lines.ToList());
            
            Logs.Debug($"Text file contents collected: {fileName}");
        }

        private async Task ProcessCollectedFiles(string taskId)
        {
            var task = _tasks[taskId];
            Logs.Info($"Processing collected files");
            
            foreach (var fileEntry in task.InMemoryFiles)
            {
                string filePath = fileEntry.Key;
                List<string> lines = fileEntry.Value;
                
                // Process each line in the file
                List<string> processedLines = new List<string>();
                foreach (var line in lines)
                {
                    processedLines.Add(ProcessWildcardLine(line, taskId));
                }
                
                // Regular text file
                string outputPath = Path.Combine(destinationFolder, task.Prefix + filePath);
                if (!outputPath.EndsWith(".txt", StringComparison.InvariantCultureIgnoreCase))
                {
                    outputPath += ".txt";
                }

                Logs.Debug($"Writing processed lines to: {outputPath}");
                
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                await File.WriteAllLinesAsync(outputPath, processedLines);
                task.OutFilesProcessed++;
            }
            
            Logs.Info($"Completed processing all files with wildcard references");
        }

        private string ProcessWildcardLine(string line, string taskId)
        {
            // See https://github.com/adieyal/sd-dynamic-prompts/blob/main/docs/SYNTAX.md#wildcards
            // Replace __wildcards__
            // Matches should include:
            // 1. __word__
            // 2. 32$$__word__  (this means pick 32 from the wildcard defined by word)
            // 3. 10-32$$__word__  (this means pick 10-32 from the wildcard defined by word)
            var wildcardPattern = @"((\d+(?:-\d+)?)?\\$\\$)?__(.+?)__";
            line = System.Text.RegularExpressions.Regex.Replace(line, wildcardPattern, match => 
            {
                string quantitySpec = match.Groups[2].Success ? match.Groups[2].Value : "";
                string reference = match.Groups[3].Value;
                return ProcessWildcardRef(quantitySpec, reference, taskId);
            });
            
            line = ProcessVariables(line, _tasks[taskId]);

            // See https://github.com/adieyal/sd-dynamic-prompts/blob/main/docs/SYNTAX.md#variants
            // Replace {} variants.
            // Can come in all these variations:
            // : Simple variants: {a|b|c}  ==> <random:a|b|c>   (the options a,b,c can be any string that does not include | or >)
            // : Empty options.  These are all valid: {a||c}  {a|b|}  {|b|c}  ==> Just like previous.  Use <comment:empty> to represent empty values.  So {a||c|} becomes <random:a|<comment:empty>|c|<comment:empty>>
            // : quantifier.  Pick 2 from a|b|c: {2$$a|b|c}  ==> <random[2,]:a|b|c>  (note the comma after the quantifier)
            // : range.  Pick 2-3 from a|b|c: {2-3$$a|b|c}  ==> <random[2-3,]:a|b|c> (note the comma after the quantifier)
            // : no lower bound.  Pick 2 or less: {-2$$a|b|c} ==> <random[1-2,]:a|b|c>   (always use 1 as lower bound)
            // : no upper bound.  Pick 2 or more: {2-$$a|b|c|d}  ==> <random[2-3,]:a|b|c|d>  (always use count-1 as upper bound)
            // : range/quantifier with custom separator.  Pick 2-3 from a|b|c|d: {2-3$$ and $$a|b|c|d}  (" and " is the custom separator)  ==> <random[2-3,]:a|b|c|d>  Ignore the custom separator.  Log a warning that we do not support it. 
            // : weighted options by prepending a number and :: before an option: {a|0.5::b|0.75::c}  The weight is 1 for a, 0.5 for b, and 0.75 for c  ==> <random:a|a|b|b|b|c|c|c|c>
            //      We cannot emit weights.  In our system, all options have weight=1.  So when we encounter this we need to compute the LCM and emit copies of each option so that when you add up all the 1 weights, you get the same relative weights as the original  

            // Process all variants with proper brace matching, including nested variants
            return ProcessVariants(line, _tasks[taskId]);
        }

        private string ProcessVariables(string input, ProcessingTask task)
        {
            /*
             * Variable assignments look like:
             * - ${variable_name=value} (deferred execution)  -> <setmacro[variable_name,false]:value>
             * - ${variable_name=!value} (immediate execution) -> <setvar[variable_name,false]:value><setmacro[variable_name,false]:<var:variable_name>>
             *
             * Variable access looks like:
             * - ${variable_name} -> <macro:variable_name>
             */
            var result = new StringBuilder(input);
            var startIndex = 0;
            
            while (true)
            {
                // Find the next variable pattern
                var varStartIndex = result.ToString().IndexOf("${", startIndex, StringComparison.Ordinal);
                if (varStartIndex == -1)
                    break;
                
                // Find the matching closing brace
                int openBraceIndex = varStartIndex + 1; // +1 to get the index of the actual '{' character
                int closeBraceIndex = FindMatchingClosingBrace(result.ToString(), openBraceIndex);
                
                if (closeBraceIndex == -1)
                {
                    // No matching closing brace found, move past this opening and continue
                    startIndex = varStartIndex + 2;
                    continue;
                }
                
                // Extract the variable content (without the ${})
                string varContent = result.ToString().Substring(varStartIndex + 2, closeBraceIndex - varStartIndex - 2);
                string replacement;
                
                // Check if this is a variable assignment or access
                int equalsIndex = varContent.IndexOf('=');
                if (equalsIndex != -1)
                {
                    // Variable assignment
                    string varName = varContent.Substring(0, equalsIndex);
                    string varValue = varContent.Substring(equalsIndex + 1);
                    
                    // Immediate or deferred?
                    if (varValue.StartsWith("!"))
                    {
                        varValue = varValue.Substring(1);
                        replacement = $"<setvar[{varName},false]:{varValue}><setmacro[{varName},false]:<var:{varName}>>";
                    }
                    else
                    {
                        // deferred
                        replacement = $"<setmacro[{varName},false]:{varValue}>";
                    }
                }
                else
                {
                    // Variable access
                    replacement = $"<macro:{varContent}>";
                }
                
                // Replace the entire variable expression with the new format
                result.Remove(varStartIndex, closeBraceIndex - varStartIndex + 1);
                result.Insert(varStartIndex, replacement);
                
                // Update the start index for the next search
                startIndex = varStartIndex + replacement.Length;
            }
            
            return result.ToString();
        }

        private string ProcessVariants(string input, ProcessingTask task)
        {
            bool hasVariants = true;
            string result = input;
            
            // Keep processing until no more variants are found
            while (hasVariants)
            {
                // Find the next variant
                (bool found, string processed) = FindAndProcessNextVariant(result, task);
                hasVariants = found;
                if (found)
                {
                    result = processed;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Finds and processes the next variant in the input string.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>A tuple containing: (whether a variant was found, the processed string)</returns>
        private (bool found, string processed) FindAndProcessNextVariant(string input, ProcessingTask task)
        {
            // Find the next opening brace
            int openBraceIndex = input.IndexOf('{');
            if (openBraceIndex == -1)
                return (false, input);
            
            // Find the matching closing brace
            int closeBraceIndex = FindMatchingClosingBrace(input, openBraceIndex);
            if (closeBraceIndex == -1)
                return (false, input);
            
            // Extract the content inside the braces
            string fullMatch = input.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1);
            string content = input.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
            
            // Process the content
            string replacement = ProcessVariantContent(content, fullMatch, task);
            
            // Replace the variant in the original string
            string result = input.Substring(0, openBraceIndex) + replacement + input.Substring(closeBraceIndex + 1);
            
            return (true, result);
        }

        /// <summary>
        /// Finds the matching closing brace for an opening brace at the specified index.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="openBraceIndex">The index of the opening brace.</param>
        /// <returns>The index of the matching closing brace, or -1 if not found.</returns>
        private int FindMatchingClosingBrace(string input, int openBraceIndex)
        {
            int braceLevel = 1;
            
            for (int i = openBraceIndex + 1; i < input.Length; i++)
            {
                if (input[i] == '{')
                    braceLevel++;
                else if (input[i] == '}')
                {
                    braceLevel--;
                    if (braceLevel == 0)
                    {
                        return i;
                    }
                }
            }
            
            return -1;
        }

        /// <summary>
        /// Processes the content of a variant.
        /// </summary>
        /// <param name="content">The content inside the braces.</param>
        /// <param name="fullMatch">The full match including braces, used for error reporting.</param>
        /// <returns>The processed content.</returns>
        private string ProcessVariantContent(string content, string fullMatch, ProcessingTask task)
        {
            // Check if it has a quantifier
            if (content.Contains("$$"))
            {
                return ProcessQuantifierVariant(content, fullMatch, task);
            }
            else
            {
                // Simple variant without quantifier
                return ProcessVariantOptions(content, "");
            }
        }

        /// <summary>
        /// Processes a variant with a quantifier.
        /// </summary>
        /// <param name="content">The content inside the braces.</param>
        /// <param name="fullMatch">The full match including braces, used for error reporting.</param>
        /// <returns>The processed content.</returns>
        private string ProcessQuantifierVariant(string content, string fullMatch, ProcessingTask task)
        {
            // Try to match the quantifier pattern
            var match = System.Text.RegularExpressions.Regex.Match(content, @"^((?:\d+-\d+|-\d+|\d+-|\d+))\$\$(.*?)(?:\$\$)?(.*)$");
            
            if (match.Success)
            {
                string quantifierSpec = "";
                string numberPart = match.Groups[1].Value;
                string customSeparator = match.Groups[2].Value;
                string optionsPart = match.Groups[3].Value;
                
                // Log warning if custom separator is found
                if (!string.IsNullOrEmpty(customSeparator))
                {
                    task.AddWarning($"Custom separator in variant not supported: {fullMatch}");
                }
                
                // Handle ranges
                if (numberPart.Contains("-"))
                {
                    quantifierSpec = ProcessRangeQuantifier(numberPart, optionsPart);
                }
                else
                {
                    // Simple quantifier: {2$$...}
                    quantifierSpec = $"[{numberPart},]";
                }
                
                return ProcessVariantOptions(optionsPart, quantifierSpec);
            }
            else
            {
                // If it doesn't match the quantifier pattern, treat it as a simple variant
                return ProcessVariantOptions(content, "");
            }
        }

        /// <summary>
        /// Processes a range quantifier.
        /// </summary>
        /// <param name="numberPart">The number part of the quantifier.</param>
        /// <param name="optionsPart">The options part of the variant.</param>
        /// <returns>The processed quantifier specification.</returns>
        private string ProcessRangeQuantifier(string numberPart, string optionsPart)
        {
            var rangeParts = numberPart.Split('-');
            string lowerBound = rangeParts[0];
            string upperBound = rangeParts.Length > 1 ? rangeParts[1] : "";
            
            // No lower bound case: {-2$$...}
            if (string.IsNullOrEmpty(lowerBound))
            {
                lowerBound = "1";
            }
            
            // No upper bound case: {2-$$...}
            if (string.IsNullOrEmpty(upperBound))
            {
                // Count options to set upper bound
                int optionCount = optionsPart.Split('|').Length;
                upperBound = optionCount.ToString();
            }
            
            return $"[{lowerBound}-{upperBound},]";
        }

        private string ProcessWildcardRef(string quantityString, string reference, string taskId)
        {
            var task = _tasks[taskId];
            // Check if reference contains glob patterns (* or **)
            if (reference.Contains('*'))
            {
                return ProcessGlobWildcardRef(quantityString, reference, taskId);
            }
            
            // Standard non-glob reference
            if (string.IsNullOrEmpty(quantityString))
            {
                return $"<wildcard:{task.Prefix + reference}>";
            }
            return $"<wildcard[{quantityString}]:{task.Prefix + reference}>";
        }
        
        private string ProcessGlobWildcardRef(string quantityString, string reference, string taskId)
        {
            var task = _tasks[taskId];
            
            // Find all matches for the glob pattern
            List<string> matchingPaths = FindMatchingPaths(reference, task.InMemoryFiles);
            
            if (!matchingPaths.Any())
            {
                task.AddWarning($"No matches found for glob pattern: {reference}");
                // Return the original reference in a way that shows it's a failed glob
                return $"<wildcard:{task.Prefix + reference}><comment:no glob matches>";
            }
            
            // Convert to a <random> tag with multiple <wildcard> entries
            if (matchingPaths.Count == 1)
            {
                // Only one match, no need for random
                string path = matchingPaths.First();
                if (string.IsNullOrEmpty(quantityString))
                {
                    return $"<wildcard:{task.Prefix + path}>";
                }
                return $"<wildcard[{quantityString},]:{task.Prefix + path}>";
            }
            
            // Build a <random> tag with all matches
            var quantitySpec = string.IsNullOrEmpty(quantityString) ? "" : $"[{quantityString},]";
            StringBuilder result = new StringBuilder();
            result.Append($"<random{quantitySpec}:");
            
            for (int i = 0; i < matchingPaths.Count; i++)
            {
                string path = matchingPaths[i];
                result.Append($"<wildcard:{task.Prefix + path}>");
                if (i < matchingPaths.Count - 1)
                {
                    result.Append("|");
                }
            }
            
            result.Append(">");
            return result.ToString();
        }
        
        private List<string> FindMatchingPaths(string globPattern, ConcurrentDictionary<string, List<string>> inMemoryFiles)
        {
            List<string> matchingPaths = new List<string>();
            
            // Check if pattern contains recursive globbing (**)
            bool isRecursive = globPattern.Contains("**");
            
            foreach (var path in inMemoryFiles.Keys)
            {
                if (IsGlobMatch(path, globPattern, isRecursive))
                {
                    matchingPaths.Add(path);
                }
            }
            
            return matchingPaths;
        }
        
        private bool IsGlobMatch(string path, string pattern, bool isRecursive)
        {
            // Convert glob pattern to regex pattern
            // Process the pattern manually instead of using Regex.Escape to handle * and ** correctly
            StringBuilder regexPattern = new StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*') // ** pattern
                    {
                        regexPattern.Append(".*");
                        i++; // Skip the next * since we've already processed it
                    }
                    else // Single * pattern
                    {
                        regexPattern.Append("[^/]*");
                    }
                }
                else
                {
                    // Escape special regex characters
                    if ("[](){}+?^$.|\\".Contains(c))
                    {
                        regexPattern.Append('\\');
                    }
                    regexPattern.Append(c);
                }
            }
            
            return Regex.IsMatch(path, $"^{regexPattern}$", RegexOptions.IgnoreCase);
        }

        private string ProcessVariantOptions(string optionsPart, string quantifierSpec)
        {
            string[] options = optionsPart.Split('|');
            
            // Check if we have weighted options
            bool hasWeightedOptions = false;
            foreach (string option in options)
            {
                if (option.Contains("::") && option.IndexOf("::", StringComparison.Ordinal) > 0)
                {
                    string weightPart = option.Substring(0, option.IndexOf("::", StringComparison.Ordinal));
                    if (double.TryParse(weightPart, out _))
                    {
                        hasWeightedOptions = true;
                        break;
                    }
                }
            }
            
            if (hasWeightedOptions)
            {
                var weightedOptions = ProcessWeightedOptions(options);
                return $"<random{quantifierSpec}:{string.Join("|", weightedOptions)}>";
            }
            
            // Handle empty options
            for (int i = 0; i < options.Length; i++)
            {
                if (string.IsNullOrEmpty(options[i]))
                {
                    options[i] = "<comment:empty>";
                }
            }

            if (quantifierSpec == "" && options.Length == 1)
            {
                return options[0];
            }

            if (quantifierSpec == "" && options.Length == 0)
            {
                return "";
            }

            return $"<random{quantifierSpec}:{string.Join("|", options)}>";
        }
        
        private string[] ProcessWeightedOptions(string[] options)
        {
            var result = new List<string>();
            var weights = new List<double>();
            var unweightedOptions = new List<string>();
            
            // Extract weights and options
            foreach (var option in options)
            {
                if (option.Contains("::") && option.IndexOf("::", StringComparison.Ordinal) > 0)
                {
                    string weightPart = option.Substring(0, option.IndexOf("::", StringComparison.Ordinal));
                    string valuePart = option.Substring(option.IndexOf("::", StringComparison.Ordinal) + 2);
                    
                    if (double.TryParse(weightPart, out double weight))
                    {
                        weights.Add(weight);
                        unweightedOptions.Add(string.IsNullOrEmpty(valuePart) ? "<comment:empty>" : valuePart);
                    }
                    else
                    {
                        // If parsing fails, treat as unweighted
                        weights.Add(1.0);
                        unweightedOptions.Add(string.IsNullOrEmpty(option) ? "<comment:empty>" : option);
                    }
                }
                else
                {
                    weights.Add(1.0);
                    unweightedOptions.Add(string.IsNullOrEmpty(option) ? "<comment:empty>" : option);
                }
            }
            
            // If all weights are 1.0, no need for special processing
            if (weights.All(w => Math.Abs(w - 1.0) < 0.001))
            {
                return unweightedOptions.ToArray();
            }
            
            // Calculate multiplier to get integer weights
            double multiplier = 1.0;
            foreach (var weight in weights)
            {
                var fractionalPart = weight - Math.Floor(weight);
                if (fractionalPart > 0)
                {
                    // Find multiplier that makes all weights integers
                    int precision = (int)Math.Pow(10, fractionalPart.ToString().TrimStart('0', '.').Length);
                    multiplier = Math.Max(multiplier, precision);
                }
            }
            
            // Convert to integer weights
            List<int> intWeights = weights.Select(w => (int)Math.Round(w * multiplier)).ToList();
            
            // Find GCD for all weights
            int gcd = intWeights.Count > 0 ? intWeights[0] : 1;
            for (int i = 1; i < intWeights.Count; i++)
            {
                gcd = GCD(gcd, intWeights[i]);
            }
            
            // Calculate normalized weights
            var normalizedWeights = intWeights.Select(w => w / gcd).ToList();
            
            // Create duplicated options according to weights
            for (int i = 0; i < unweightedOptions.Count; i++)
            {
                for (int j = 0; j < normalizedWeights[i]; j++)
                {
                    result.Add(unweightedOptions[i]);
                }
            }
            
            return result.ToArray();
        }
        
        private int GCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }
        
        private int GCD(int a, int b, params int[] numbers)
        {
            int result = GCD(a, b);
            foreach (int number in numbers)
            {
                result = GCD(result, number);
            }
            return result;
        }

        public ProgressStatus GetStatus(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                return new ProgressStatus
                {
                    Status = task.Status.ToString(),
                    Infiles = task.InFiles,
                    Outfiles = task.InMemoryFiles.Count,
                    InfilesProcessed = task.InFilesProcessed,
                    OutfilesProcessed = task.OutFilesProcessed,
                    Conflicts = task.Conflicts.ToList(),
                    Warnings = task.Warnings.Slice(0, task.Warnings.Count),
                };
            }
            return new ProgressStatus { Status = "Not Found" };
        }

        public async Task<bool> UndoProcessing(string taskId)
        {
            Logs.Info($"Undoing processing for task: {taskId}");
            if (_tasks.TryGetValue(taskId, out var task))
            {
                foreach (var backup in task.Backups)
                {
                    File.Move(backup.Value, backup.Key, true);
                }
                _tasks.TryRemove(taskId, out _);
                Logs.Info($"Processing undone for task: {taskId}");
                return true;
            }
            Logs.Warning($"Processing undo failed for task: {taskId}");
            return false;
        }

        public List<ProcessingHistoryItem> GetHistory()
        {
            return _history.OrderByDescending(h => h.Timestamp).ToList();
        }

        public async Task<bool> ResolveConflict(string taskId, string filePath, string resolution)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                Logs.Info($"Resolving conflict for task: {taskId}, filePath: {filePath}, resolution: {resolution}");
                var conflict = task.Conflicts.FirstOrDefault(c => c.FilePath == filePath);
                if (conflict != null)
                {
                    switch (resolution)
                    {
                        case "overwrite":
                            Logs.Info($"Overwriting file: {conflict.FilePath}");
                            File.Move(conflict.TempPath, conflict.FilePath, true);
                            break;
                        case "rename":
                            string newPath = GetUniqueFilePath(conflict.FilePath);
                            Logs.Info($"Renaming file: {conflict.FilePath} to {newPath}");
                            File.Move(conflict.TempPath, newPath);
                            break;
                        case "skip":
                            Logs.Info($"Skipping file: {conflict.FilePath}");
                            File.Delete(conflict.TempPath);
                            break;
                    }
                    task.Conflicts.TryTake(out _);
                    Logs.Info($"Conflict resolved for task: {taskId}, filePath: {filePath}");
                    return true;
                }
            }
            Logs.Warning($"Failed to resolve conflict for task: {taskId}, filePath: {filePath}");
            return false;
        }

        private string GetUniqueFilePath(string originalPath)
        {
            string directory = Path.GetDirectoryName(originalPath);
            string fileName = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            int counter = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                counter++;
            } while (File.Exists(newPath));

            return newPath;
        }
    }

    public class ProcessingTask
    {
        public string Id;
        public int InFiles;
        public int InFilesProcessed;
        public int OutFilesProcessed;
        public string Prefix;
        public ProcessingStatusEnum Status;
        public ConcurrentBag<string> Errors = new();
        public List<string> Warnings = new();

        public void AddWarning(string warning)
        {
            lock (Warnings)
            {
                Warnings.Add("Warning: " + warning);
                Logs.Warning($"WildcardExtension: {warning}");
            }
        }

        public ConcurrentDictionary<string, string> Backups = new();
        public ConcurrentBag<ConflictInfo> Conflicts = new();

        // In-memory structure to hold all discovered files before processing wildcard references
        public ConcurrentDictionary<string, List<string>> InMemoryFiles = new();
    }

    public struct ProgressStatus
    {
        public string Status;
        public int Infiles;
        public int Outfiles;
        public int InfilesProcessed;
        public int OutfilesProcessed;
        public List<string> Warnings;
        public List<ConflictInfo> Conflicts;
    }

    public enum ProcessingStatusEnum
    {
        InProgress,
        Completed,
        Failed,
    }

    public class ConflictInfo
    {
        public string FilePath;
        public string TempPath;
    }

    public class ProcessingHistoryItem
    {
        public string TaskId;
        public DateTime Timestamp;
        public string Description;
        public string Name;
        public List<string> Warnings;
        public bool Success;
    }

    /// <summary>Represents file data sent from the frontend. Can be a file path or a base64 encoded file. This is represented as a file path if the Base64Content is null, and as a base64 encoded file otherwise.</summary>
    public class FileData
    {
        #pragma warning disable CS8632 // Nullable reference annotations used without nullable context
        public string FilePath;
        public string? Base64Content;
        #pragma warning restore CS8632 // Nullable reference annotations used without nullable context
    }
}
