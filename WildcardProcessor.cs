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
            Logs.Verbose($"WildcardProcessor: ProcessWildcardLine called with line='{line}'");
            
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
            
            // Process set commands BEFORE other processing
            line = ProcessSetCommands(line, _tasks[taskId]);
            
            // Process echo commands BEFORE other processing
            line = ProcessEchoCommands(line, _tasks[taskId]);
            
            // Process if commands BEFORE other processing
            line = ProcessIfCommands(line, _tasks[taskId]);
            
            // Process STN commands BEFORE other processing
            line = ProcessStnCommands(line, _tasks[taskId]);
            
            // Process prompt editing BEFORE negative attention to avoid conflicts
            // [from:to:step] -> <fromto[step]:from||to>
            line = ProcessPromptEditing(line, _tasks[taskId]);
            
            // Process alternating words AFTER prompt editing but BEFORE negative attention
            // [word1|word2|word3] -> <alternate:word1||word2||word3>
            line = ProcessAlternatingWords(line, _tasks[taskId]);
            
            // Process negative attention specifiers: [text] -> (text:0.9)
            line = ProcessNegativeAttention(line, taskId);

            // Replace BREAK words with <comment:empty>
            // Only replace capital BREAK that is a standalone word (word boundaries)
            // Avoid replacing BREAK inside wildcard paths, variable names, or wccase expressions
            line = ProcessBreakWords(line);

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

        private string ProcessBreakWords(string input)
        {
            // Replace BREAK words with <comment:empty>
            // Only replace capital BREAK that is a standalone word
            // Protect BREAK if it has non-space, non-comma characters on either side
            // This naturally protects wildcard paths (__path/BREAK/file__) and variable names (${BREAK})
            
            var result = new StringBuilder();
            int i = 0;
            
            while (i < input.Length)
            {
                // Check if we're at the start of "BREAK"
                if (i + 4 < input.Length && input.Substring(i, 5) == "BREAK")
                {
                    // Check if BREAK has non-space, non-comma characters on either side
                    bool hasProtectedCharBefore = i > 0 && input[i - 1] != ' ' && input[i - 1] != ',' && input[i - 1] != '\t' && input[i - 1] != '\n' && input[i - 1] != '\r';
                    bool hasProtectedCharAfter = i + 5 < input.Length && input[i + 5] != ' ' && input[i + 5] != ',' && input[i + 5] != '\t' && input[i + 5] != '\n' && input[i + 5] != '\r';
                    
                    // Only replace BREAK if it doesn't have protected characters on either side
                    if (!hasProtectedCharBefore && !hasProtectedCharAfter)
                    {
                        // Replace BREAK with <comment:empty>
                        result.Append("<comment:empty>");
                        i += 5; // Skip past "BREAK"
                        continue;
                    }
                }
                
                result.Append(input[i]);
                i++;
            }
            
            return result.ToString();
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
                
                // Check for completely empty variable content: ${}
                if (string.IsNullOrWhiteSpace(varContent))
                {
                    // Return original malformed syntax - move past this and continue
                    startIndex = varStartIndex + 2;
                    continue;
                }
                // Check if this is a variable assignment or access
                else if (varContent.IndexOf('=') != -1)
                {
                    int equalsIndex = varContent.IndexOf('=');
                    // Variable assignment - check for special operators
                    string varName = varContent.Substring(0, equalsIndex);
                    string varValue = varContent.Substring(equalsIndex + 1);
                    
                    // Check for add operator (+=)
                    if (varName.EndsWith("+"))
                    {
                        varName = varName.Substring(0, varName.Length - 1);
                        
                        // Immediate add or deferred add?
                        if (varValue.StartsWith("!"))
                        {
                            varValue = varValue.Substring(1);
                            // Immediate add: ${var+=!value} -> <setvar[var,false]:<macro:var>, value><setmacro[var,false]:<var:var>>
                            replacement = $"<setvar[{varName},false]:<macro:{varName}>, {varValue}><setmacro[{varName},false]:<var:{varName}>>";
                        }
                        else
                        {
                            // Deferred add: ${var+=value} -> <wcaddmacro[var]:, value>
                            replacement = $"<wcaddmacro[{varName}]:, {varValue}>";
                        }
                    }
                    // Check for ifundefined operator (?=)
                    else if (varName.EndsWith("?"))
                    {
                        varName = varName.Substring(0, varName.Length - 1);
                        
                        // Immediate ifundefined or deferred ifundefined?
                        if (varValue.StartsWith("!"))
                        {
                            varValue = varValue.Substring(1);
                            // Immediate ifundefined: ${var?=!value} -> <wcmatch:<wccase[length(var) eq 0]:<setvar[var,false]:value><setmacro[var,false]:<var:var>>>>
                            replacement = $"<wcmatch:<wccase[length({varName}) eq 0]:<setvar[{varName},false]:{varValue}><setmacro[{varName},false]:<var:{varName}>>>>";
                        }
                        else
                        {
                            // Deferred ifundefined: ${var?=value} -> <wcmatch:<wccase[length(var) eq 0]:<setmacro[var,false]:value>>>
                            replacement = $"<wcmatch:<wccase[length({varName}) eq 0]:<setmacro[{varName},false]:{varValue}>>>";
                        }
                    }
                    else
                    {
                        // Regular assignment
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
                }
                else
                {
                    // Variable access - check for default value syntax: ${var:default}
                    int colonIndex = varContent.IndexOf(':');
                    if (colonIndex != -1)
                    {
                        // Variable with default: ${var:default}
                        string variableName = varContent.Substring(0, colonIndex).Trim();
                        string defaultValue = varContent.Substring(colonIndex + 1);
                        
                        // Check for malformed cases (empty variable name)
                        if (string.IsNullOrWhiteSpace(variableName))
                        {
                            // Return original malformed syntax - move past this and continue
                            startIndex = varStartIndex + 2;
                            continue;
                        }
                        else if (string.IsNullOrEmpty(defaultValue))
                        {
                            // Empty default, just return <macro:varname>
                            replacement = $"<macro:{variableName}>";
                        }
                        else
                        {
                            // Process the default value recursively to handle nested syntax
                            string processedDefault = ProcessWildcardLine(defaultValue, task.Id);
                            
                            // Return wcmatch with condition for empty variable and else clause
                            replacement = $"<wcmatch:<wccase[length({variableName}) eq 0]:{processedDefault}><wccase:<macro:{variableName}>>>";
                        }
                    }
                    else
                    {
                        // Simple variable access: ${var}
                        replacement = $"<macro:{varContent}>";
                    }
                }
                
                // Replace the entire variable expression with the new format
                result.Remove(varStartIndex, closeBraceIndex - varStartIndex + 1);
                result.Insert(varStartIndex, replacement);
                
                // Update the start index for the next search
                startIndex = varStartIndex + replacement.Length;
            }
            
            return result.ToString();
        }

        private string ProcessSetCommands(string input, ProcessingTask task)
        {
            /*
             * Long-form set commands look like:
             * - <ppp:set varname>value<ppp:/set> -> <setmacro[varname,false]:value>
             * - <ppp:set varname evaluate>value<ppp:/set> -> <setvar[varname,false]:value><setmacro[varname,false]:<var:varname>>
             * - <ppp:set varname add>value<ppp:/set> -> <wcaddmacro[varname]:, value>
             * - <ppp:set varname evaluate add>value<ppp:/set> -> <setvar[varname,false]:<macro:varname>, value><setmacro[varname,false]:<var:varname>>
             * - <ppp:set varname ifundefined>value<ppp:/set> -> <wcmatch:<wccase[length(varname) eq 0]:<setmacro[varname,false]:value>>>
             * - <ppp:set varname evaluate ifundefined>value<ppp:/set> -> <wcmatch:<wccase[length(varname) eq 0]:<setvar[varname,false]:value><setmacro[varname,false]:<var:varname>>>>
             */
            var result = new StringBuilder(input);
            var startIndex = 0;
            
            while (true)
            {
                // Find the next set command pattern
                var setStartIndex = result.ToString().IndexOf("<ppp:set ", startIndex, StringComparison.Ordinal);
                if (setStartIndex == -1)
                    break;
                
                // Find the matching closing tag
                var setEndIndex = result.ToString().IndexOf("<ppp:/set>", setStartIndex, StringComparison.Ordinal);
                if (setEndIndex == -1)
                {
                    // No matching closing tag found, move past this opening and continue
                    task.AddWarning("Malformed set command: no closing <ppp:/set> tag found");
                    startIndex = setStartIndex + 9; // length of "<ppp:set "
                    continue;
                }
                
                // Extract the set command content
                string setContent = result.ToString().Substring(setStartIndex + 9, setEndIndex - setStartIndex - 9); // 9 = length of "<ppp:set "
                
                // Find where the variable name and modifiers end (look for >)
                int valueStartIndex = setContent.IndexOf('>');
                if (valueStartIndex == -1)
                {
                    // Malformed set command, skip it
                    task.AddWarning("Malformed set command missing closing >: <ppp:set " + setContent + "<ppp:/set>");
                    startIndex = setStartIndex + 9;
                    continue;
                }
                
                string varAndModifiers = setContent.Substring(0, valueStartIndex).Trim();
                string value = setContent.Substring(valueStartIndex + 1);
                
                // Handle empty values
                if (string.IsNullOrEmpty(value))
                {
                    value = "<comment:empty>";
                }
                
                // Parse variable name and modifiers
                string[] parts = varAndModifiers.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    // No variable name, skip
                    task.AddWarning("Malformed set command: no variable name specified: <ppp:set " + setContent + "<ppp:/set>");
                    startIndex = setStartIndex + 9;
                    continue;
                }
                
                string varName = parts[0];
                var modifiers = new HashSet<string>(parts.Skip(1), StringComparer.OrdinalIgnoreCase);
                
                // Check for invalid modifier combinations
                if (modifiers.Contains("add") && modifiers.Contains("ifundefined"))
                {
                    task.AddWarning("Invalid modifier combination: 'add' and 'ifundefined' cannot be used together");
                    // Leave as literal text
                    startIndex = setEndIndex + 10; // length of "<ppp:/set>"
                    continue;
                }
                
                string replacement;
                
                // Generate appropriate replacement based on modifiers
                if (modifiers.Contains("ifundefined"))
                {
                    if (modifiers.Contains("evaluate"))
                    {
                        // Immediate ifundefined: <wcmatch:<wccase[length(varname) eq 0]:<setvar[varname,false]:value><setmacro[varname,false]:<var:varname>>>>
                        replacement = $"<wcmatch:<wccase[length({varName}) eq 0]:<setvar[{varName},false]:{value}><setmacro[{varName},false]:<var:{varName}>>>>";
                    }
                    else
                    {
                        // Deferred ifundefined: <wcmatch:<wccase[length(varname) eq 0]:<setmacro[varname,false]:value>>>
                        replacement = $"<wcmatch:<wccase[length({varName}) eq 0]:<setmacro[{varName},false]:{value}>>>";
                    }
                }
                else if (modifiers.Contains("add"))
                {
                    if (modifiers.Contains("evaluate"))
                    {
                        // Immediate add: <setvar[varname,false]:<macro:varname>, value><setmacro[varname,false]:<var:varname>>
                        replacement = $"<setvar[{varName},false]:<macro:{varName}>, {value}><setmacro[{varName},false]:<var:{varName}>>";
                    }
                    else
                    {
                        // Deferred add: <wcaddmacro[varname]:, value>
                        replacement = $"<wcaddmacro[{varName}]:, {value}>";
                    }
                }
                else
                {
                    if (modifiers.Contains("evaluate"))
                    {
                        // Immediate assignment: <setvar[varname,false]:value><setmacro[varname,false]:<var:varname>>
                        replacement = $"<setvar[{varName},false]:{value}><setmacro[{varName},false]:<var:{varName}>>";
                    }
                    else
                    {
                        // Deferred assignment: <setmacro[varname,false]:value>
                        replacement = $"<setmacro[{varName},false]:{value}>";
                    }
                }
                
                // Replace the entire set command with the new format
                int totalLength = setEndIndex - setStartIndex + 10; // +10 for "<ppp:/set>"
                result.Remove(setStartIndex, totalLength);
                result.Insert(setStartIndex, replacement);
                
                // Update the start index for the next search
                startIndex = setStartIndex + replacement.Length;
            }
            
            return result.ToString();
        }

        private string ProcessAlternatingWords(string input, ProcessingTask task)
        {
            var result = new StringBuilder(input);
            var startIndex = 0;
            
            while (true)
            {
                // Find the next bracket that could be alternating words
                var bracketStartIndex = result.ToString().IndexOf("[", startIndex, StringComparison.Ordinal);
                if (bracketStartIndex == -1)
                    break;
                
                // Check if this bracket is escaped
                if (bracketStartIndex > 0 && result[bracketStartIndex - 1] == '\\')
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Check if this bracket is part of SwarmUI syntax (inside < >)
                if (IsInsideSwarmUITag(result.ToString(), bracketStartIndex))
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Find the matching closing bracket
                int closeBracketIndex = FindMatchingClosingSquareBracket(result.ToString(), bracketStartIndex);
                
                if (closeBracketIndex == -1)
                {
                    // No matching closing bracket found, move past this opening and continue
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Extract the content inside the brackets (without the [])
                string content = result.ToString().Substring(bracketStartIndex + 1, closeBracketIndex - bracketStartIndex - 1);
                
                // Check if this is alternating words syntax: [word1|word2|...]
                if (IsAlternatingWordsSyntax(content))
                {
                    // Process as alternating words
                    string replacement = ProcessAlternatingWordsContent(content, task);
                    
                    // Replace the entire alternating words expression with the new format
                    result.Remove(bracketStartIndex, closeBracketIndex - bracketStartIndex + 1);
                    result.Insert(bracketStartIndex, replacement);
                    
                    // Update the start index for the next search
                    startIndex = bracketStartIndex + replacement.Length;
                }
                else
                {
                    // Not alternating words, skip this bracket
                    startIndex = bracketStartIndex + 1;
                }
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Checks if the content inside brackets matches alternating words syntax: word1|word2|...
        /// Must have at least 2 options separated by pipes at the top level.
        /// </summary>
        /// <param name="content">The content inside the brackets.</param>
        /// <returns>True if it's alternating words syntax, false otherwise.</returns>
        private bool IsAlternatingWordsSyntax(string content)
        {
            // Find pipes at the top level (not inside nested structures)
            char[] openChars = { '{', '[', '<' };
            char[] closeChars = { '}', ']', '>' };
            
            var pipeIndices = new List<int>();
            int searchStart = 0;
            
            while (true)
            {
                int pipeIndex = FindTopLevelChar(content, searchStart, '|', openChars, closeChars);
                if (pipeIndex == -1)
                    break;
                    
                pipeIndices.Add(pipeIndex);
                searchStart = pipeIndex + 1;
            }
            
            // Must have at least 1 pipe (meaning at least 2 options) for alternating words
            // If no pipes, it should be treated as negative attention
            return pipeIndices.Count >= 1;
        }
        
        /// <summary>
        /// Processes alternating words content and converts it to SwarmUI syntax.
        /// </summary>
        /// <param name="content">The content inside the brackets: word1|word2|word3</param>
        /// <param name="task">The processing task for context.</param>
        /// <returns>The converted SwarmUI syntax: &lt;alternate:word1||word2||word3&gt;</returns>
        private string ProcessAlternatingWordsContent(string content, ProcessingTask task)
        {
            // Split on pipes at the top level
            char[] openChars = { '{', '[', '<' };
            char[] closeChars = { '}', ']', '>' };
            
            var options = new List<string>();
            int lastIndex = 0;
            
            while (true)
            {
                int pipeIndex = FindTopLevelChar(content, lastIndex, '|', openChars, closeChars);
                if (pipeIndex == -1)
                {
                    // Add the last option
                    string lastOption = content.Substring(lastIndex);
                    if (string.IsNullOrEmpty(lastOption.Trim()))
                    {
                        options.Add("<comment:empty>");
                    }
                    else
                    {
                        // Recursively process the option to handle nested syntax
                        options.Add(ProcessWildcardLine(lastOption, task.Id));
                    }
                    break;
                }
                
                // Add the current option
                string option = content.Substring(lastIndex, pipeIndex - lastIndex);
                if (string.IsNullOrEmpty(option.Trim()))
                {
                    options.Add("<comment:empty>");
                }
                else
                {
                    // Recursively process the option to handle nested syntax
                    options.Add(ProcessWildcardLine(option, task.Id));
                }
                
                lastIndex = pipeIndex + 1;
            }
            
            // Join with double pipes for SwarmUI alternate syntax
            return $"<alternate:{string.Join("||", options)}>";
        }

        private string ProcessPromptEditing(string input, ProcessingTask task)
        {
            var result = new StringBuilder(input);
            var startIndex = 0;
            
            while (true)
            {
                // Find the next bracket that could be prompt editing
                var bracketStartIndex = result.ToString().IndexOf("[", startIndex, StringComparison.Ordinal);
                if (bracketStartIndex == -1)
                    break;
                
                // Check if this bracket is escaped
                if (bracketStartIndex > 0 && result[bracketStartIndex - 1] == '\\')
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Check if this bracket is part of SwarmUI syntax (inside < >)
                if (IsInsideSwarmUITag(result.ToString(), bracketStartIndex))
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Find the matching closing bracket
                int closeBracketIndex = FindMatchingClosingSquareBracket(result.ToString(), bracketStartIndex);
                
                if (closeBracketIndex == -1)
                {
                    // No matching closing bracket found, move past this opening and continue
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Extract the content inside the brackets (without the [])
                string content = result.ToString().Substring(bracketStartIndex + 1, closeBracketIndex - bracketStartIndex - 1);
                
                // Check if this is prompt editing syntax: [from:to:step]
                if (IsPromptEditingSyntax(content))
                {
                    // Process as prompt editing
                    string replacement = ProcessPromptEditingContent(content, task);
                    
                    // Replace the entire prompt editing expression with the new format
                    result.Remove(bracketStartIndex, closeBracketIndex - bracketStartIndex + 1);
                    result.Insert(bracketStartIndex, replacement);
                    
                    // Update the start index for the next search
                    startIndex = bracketStartIndex + replacement.Length;
                }
                else
                {
                    // Not prompt editing, skip this bracket
                    startIndex = bracketStartIndex + 1;
                }
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Checks if the content inside brackets matches prompt editing syntax: from:to:step
        /// where step must be a valid number.
        /// </summary>
        /// <param name="content">The content inside the brackets.</param>
        /// <returns>True if it's prompt editing syntax, false otherwise.</returns>
        private bool IsPromptEditingSyntax(string content)
        {
            // Find colons at the top level (not inside nested structures)
            char[] openChars = { '{', '[', '<' };
            char[] closeChars = { '}', ']', '>' };
            
            var colonIndices = new List<int>();
            int searchStart = 0;
            
            while (true)
            {
                int colonIndex = FindTopLevelChar(content, searchStart, ':', openChars, closeChars);
                if (colonIndex == -1)
                    break;
                    
                colonIndices.Add(colonIndex);
                searchStart = colonIndex + 1;
            }
            
            // Must have exactly 2 colons for prompt editing syntax [a:b:c]
            // We don't validate what 'c' contains - let SwarmUI validate parameters at runtime
            return colonIndices.Count == 2;
        }
        
        /// <summary>
        /// Processes prompt editing content and converts it to SwarmUI syntax.
        /// </summary>
        /// <param name="content">The content inside the brackets: from:to:step</param>
        /// <param name="task">The processing task for context.</param>
        /// <returns>The converted SwarmUI syntax: &lt;fromto[step]:from||to&gt;</returns>
        private string ProcessPromptEditingContent(string content, ProcessingTask task)
        {
            // Find colons at the top level
            char[] openChars = { '{', '[', '<' };
            char[] closeChars = { '}', ']', '>' };
            
            var colonIndices = new List<int>();
            int searchStart = 0;
            
            while (true)
            {
                int colonIndex = FindTopLevelChar(content, searchStart, ':', openChars, closeChars);
                if (colonIndex == -1)
                    break;
                    
                colonIndices.Add(colonIndex);
                searchStart = colonIndex + 1;
            }
            
            // Extract the three parts
            string fromPart = content.Substring(0, colonIndices[0]);
            string toPart = content.Substring(colonIndices[0] + 1, colonIndices[1] - colonIndices[0] - 1);
            string stepPart = content.Substring(colonIndices[1] + 1);
            
            // Recursively process from, to, and step parts to handle nested syntax
            string processedFrom = string.IsNullOrEmpty(fromPart.Trim()) ? "<comment:empty>" : ProcessWildcardLine(fromPart, task.Id);
            string processedTo = string.IsNullOrEmpty(toPart.Trim()) ? "<comment:empty>" : ProcessWildcardLine(toPart, task.Id);
            string processedStep = string.IsNullOrEmpty(stepPart.Trim()) ? "<comment:empty>" : ProcessWildcardLine(stepPart, task.Id);
            
            return $"<fromto[{processedStep}]:{processedFrom}||{processedTo}>";
        }

        private string ProcessNegativeAttention(string input, string taskId)
        {
            // Use a recursive approach to handle nested brackets properly
            return ProcessNegativeAttentionRecursive(input, taskId);
        }
        
        private string ProcessNegativeAttentionRecursive(string input, string taskId)
        {
            var result = new StringBuilder(input);
            var startIndex = 0;
            
            while (true)
            {
                // Find the next negative attention pattern
                var bracketStartIndex = result.ToString().IndexOf("[", startIndex, StringComparison.Ordinal);
                if (bracketStartIndex == -1)
                    break;
                
                // Check if this bracket is escaped
                if (bracketStartIndex > 0 && result[bracketStartIndex - 1] == '\\')
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Check if this bracket is part of SwarmUI syntax (inside < >)
                if (IsInsideSwarmUITag(result.ToString(), bracketStartIndex))
                {
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Find the matching closing bracket for this level
                int closeBracketIndex = FindMatchingClosingSquareBracket(result.ToString(), bracketStartIndex);
                
                if (closeBracketIndex == -1)
                {
                    // No matching closing bracket found, move past this opening and continue
                    startIndex = bracketStartIndex + 1;
                    continue;
                }
                
                // Extract the content inside the brackets (without the [])
                string content = result.ToString().Substring(bracketStartIndex + 1, closeBracketIndex - bracketStartIndex - 1);
                
                // Recursively process the content first to handle nested brackets
                string processedContent = ProcessNegativeAttentionRecursive(content, taskId);
                
                // Then process wildcard constructs within the content
                processedContent = ProcessWildcardLine(processedContent, taskId);
                
                // Calculate the weight - if already processed, compound it
                double weight = 0.9;
                string finalContent = processedContent;
                
                // Check if the processed content is already an attention specifier
                var attentionMatch = System.Text.RegularExpressions.Regex.Match(processedContent, @"^\((.+?):([0-9.]+)\)$");
                if (attentionMatch.Success)
                {
                    finalContent = attentionMatch.Groups[1].Value;
                    if (double.TryParse(attentionMatch.Groups[2].Value, out double existingWeight))
                    {
                        weight = existingWeight * 0.9; // Compound the weight
                    }
                }
                
                // Transform to ComfyUI positive attention format
                string replacement = $"({finalContent}:{weight:0.###})";
                
                // Replace the entire negative attention expression with the new format
                result.Remove(bracketStartIndex, closeBracketIndex - bracketStartIndex + 1);
                result.Insert(bracketStartIndex, replacement);
                
                // Update the start index for the next search
                startIndex = bracketStartIndex + replacement.Length;
            }
            
            return result.ToString();
        }

        /// <summary>
        /// Checks if a bracket at the given index is inside a SwarmUI tag (between < and >).
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="bracketIndex">The index of the bracket to check.</param>
        /// <returns>True if the bracket is inside a SwarmUI tag, false otherwise.</returns>
        private bool IsInsideSwarmUITag(string input, int bracketIndex)
        {
            // Track nesting level of angle brackets to properly handle nested SwarmUI tags
            int angleDepth = 0;
            
            for (int i = 0; i < bracketIndex; i++)
            {
                if (input[i] == '<')
                {
                    angleDepth++;
                }
                else if (input[i] == '>')
                {
                    angleDepth--;
                }
            }
            
            // If angleDepth > 0, we're inside SwarmUI tags
            return angleDepth > 0;
        }

        /// <summary>
        /// Finds the matching closing square bracket for an opening bracket at the specified index.
        /// Handles nested square brackets properly by counting bracket depth.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="openBracketIndex">The index of the opening square bracket.</param>
        /// <returns>The index of the matching closing square bracket, or -1 if not found.</returns>
        private int FindMatchingClosingSquareBracket(string input, int openBracketIndex)
        {
            return FindMatchingClosingBracket(input, openBracketIndex, '[', ']');
        }
        
        /// <summary>
        /// Generic method to find matching closing bracket/brace for any bracket type.
        /// Handles nested brackets properly by counting bracket depth and respects escaped characters.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="openIndex">The index of the opening bracket.</param>
        /// <param name="openChar">The opening bracket character.</param>
        /// <param name="closeChar">The closing bracket character.</param>
        /// <returns>The index of the matching closing bracket, or -1 if not found.</returns>
        private int FindMatchingClosingBracket(string input, int openIndex, char openChar, char closeChar)
        {
            int bracketLevel = 1;
            
            for (int i = openIndex + 1; i < input.Length; i++)
            {
                // Check if this bracket is escaped
                if (i > 0 && input[i - 1] == '\\')
                    continue;
                    
                if (input[i] == openChar)
                {
                    bracketLevel++;
                }
                else if (input[i] == closeChar)
                {
                    bracketLevel--;
                    if (bracketLevel == 0)
                    {
                        return i;
                    }
                }
            }
            
            return -1;
        }
        
        /// <summary>
        /// Finds the index of the next unescaped character at the top nesting level.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="startIndex">The index to start searching from.</param>
        /// <param name="targetChar">The character to find.</param>
        /// <param name="openChars">Array of opening bracket characters to track nesting.</param>
        /// <param name="closeChars">Array of closing bracket characters to track nesting.</param>
        /// <returns>The index of the target character at top level, or -1 if not found.</returns>
        private int FindTopLevelChar(string input, int startIndex, char targetChar, char[] openChars, char[] closeChars)
        {
            int[] nestingLevels = new int[openChars.Length];
            
            for (int i = startIndex; i < input.Length; i++)
            {
                // Check if this character is escaped
                if (i > 0 && input[i - 1] == '\\')
                    continue;
                
                char c = input[i];
                
                // Update nesting levels
                for (int j = 0; j < openChars.Length; j++)
                {
                    if (c == openChars[j])
                    {
                        nestingLevels[j]++;
                    }
                    else if (c == closeChars[j])
                    {
                        nestingLevels[j]--;
                    }
                }
                
                // Check if we found the target character at top level
                if (c == targetChar && nestingLevels.All(level => level == 0))
                {
                    return i;
                }
            }
            
            return -1;
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
            return FindMatchingClosingBracket(input, openBraceIndex, '{', '}');
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
                return ProcessVariantOptions(content, "", task);
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
            // Try to match the quantifier pattern with optional prefix flags and optional numeric value
            // Pattern handles: [~@ro][2]$$options or [~@ro][2]$$separator$$options
            // Optional prefix flags: ~ @ r o (can appear in any combination)
            // Optional numeric quantifier: can be empty for prefix-only variants like {ro$$a|b|c}
            var match = System.Text.RegularExpressions.Regex.Match(content, @"^(?<prefixFlags>[~@ro]*)(?<numberPart>(?:\d+-\d+|-\d+|\d+-|\d+)?)\$\$(?<remainingPart>.*)$");
            
            Logs.Debug($"WildcardProcessor: Regex match success={match.Success}");
            
            if (match.Success)
            {
                string quantifierSpec = "";
                string prefixFlags = match.Groups["prefixFlags"].Value; // Optional prefix flags (~, @, r, o) - parsed but ignored
                string numberPart = match.Groups["numberPart"].Value;
                string remainingPart = match.Groups["remainingPart"].Value;
                
                // Log debug info about prefix flags if present
                if (!string.IsNullOrEmpty(prefixFlags))
                {
                    prefixFlags = prefixFlags.Replace("~", "").Replace("o", "");
                    if (!string.IsNullOrEmpty(prefixFlags))
                    {
                        task.AddWarning($"Variant flags '{prefixFlags}' not supported.  Ignoring.");
                    }
                }
                
                // Check if there's a custom separator (pattern: separator$$options)
                // But ignore $$ inside nested structures like <wildcard:...> or {...}
                string customSeparator = "";
                string optionsPart = remainingPart;
                
                int separatorIndex = FindCustomSeparatorIndex(remainingPart);
                if (separatorIndex >= 0)
                {
                    customSeparator = remainingPart.Substring(0, separatorIndex);
                    optionsPart = remainingPart.Substring(separatorIndex + 2); // Skip the $$
                    
                    // Log warning if custom separator is found
                    if (!string.IsNullOrEmpty(customSeparator))
                    {
                        task.AddWarning($"Custom separator in variant not supported: {fullMatch}");
                    }
                }
                
                // Handle quantifier specification
                if (!string.IsNullOrEmpty(numberPart))
                {
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
                }
                else
                {
                    // No quantifier, just prefix flags: {ro$$...} - treat as simple variant
                    quantifierSpec = "";
                }
                
                // Special case: Single wildcard with quantifier should become <wcwildcard[quantifier]:name>
                // Example: {2$$__flavours__} should become <wcwildcard[2,]:flavours> not <random[2,]:<wcwildcard:flavours>>
                Logs.Debug($"WildcardProcessor: Checking single wildcard for optionsPart='{optionsPart}'");
                if (IsSingleWildcard(optionsPart))
                {
                    string wildcardName = ExtractWildcardName(optionsPart);
                    string result = $"<wcwildcard{quantifierSpec}:{wildcardName}>";
                    Logs.Debug($"WildcardProcessor: Single wildcard detected, returning '{result}'");
                    return result;
                }
                Logs.Debug($"WildcardProcessor: Not a single wildcard, proceeding with normal variant processing");
                
                return ProcessVariantOptions(optionsPart, quantifierSpec, task);
            }
            else
            {
                // If it doesn't match the quantifier pattern, treat it as a simple variant
                return ProcessVariantOptions(content, "", task);
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
            
            // Recursively process the reference to handle nested syntax like ${variable}
            string processedReference = ProcessWildcardLine(reference, taskId);
            
            // Check if reference contains glob patterns (* or **)
            if (processedReference.Contains('*'))
            {
                return ProcessGlobWildcardRef(quantityString, processedReference, taskId);
            }
            
            // Standard non-glob reference
            if (string.IsNullOrEmpty(quantityString))
            {
                return $"<wcwildcard:{task.Prefix + processedReference}>";
            }
            return $"<wcwildcard[{quantityString}]:{task.Prefix + processedReference}>";
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
                return $"<wcwildcard:{task.Prefix + reference}><comment:no glob matches>";
            }
            
            // Convert to a <wcrandom> tag with multiple <wcwildcard> entries
            if (matchingPaths.Count == 1)
            {
                // Only one match, no need for random
                string path = matchingPaths.First();
                if (string.IsNullOrEmpty(quantityString))
                {
                    return $"<wcwildcard:{task.Prefix + path}>";
                }
                return $"<wcwildcard[{quantityString},]:{task.Prefix + path}>";
            }
            
            // Build a <wcrandom> tag with all matches
            var quantitySpec = string.IsNullOrEmpty(quantityString) ? "" : $"[{quantityString},]";
            StringBuilder result = new StringBuilder();
            result.Append($"<wcrandom{quantitySpec}:");
            
            for (int i = 0; i < matchingPaths.Count; i++)
            {
                string path = matchingPaths[i];
                result.Append($"<wcwildcard:{task.Prefix + path}>");
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

        private string ProcessVariantOptions(string optionsPart, string quantifierSpec, ProcessingTask task)
        {
            string[] options = optionsPart.Split('|');
            
            // Handle empty options and recursively process each option
            for (int i = 0; i < options.Length; i++)
            {
                if (string.IsNullOrEmpty(options[i]))
                {
                    options[i] = "<comment:empty>";
                }
                else
                {
                    // Recursively process each option to handle nested syntax
                    options[i] = ProcessWildcardLine(options[i], task.Id);
                    // if option has opts section then trim the leading spaces
                    // look for "::" that occurs before any nested tag ("<")
                    var iopts = options[i].IndexOf("::", StringComparison.InvariantCulture);
                    if (iopts > 0 && !options[i].Substring(0, iopts).Contains("<"))
                    {
                        options[i] = options[i].TrimStart();
                    }
                }
            }

            if (quantifierSpec == "" && options.Length == 0)
            {
                return "";
            }

            // Use wcrandom which supports native weighted options (weight::option syntax)
            // No need to expand weighted options - wcrandom handles them directly
            return $"<wcrandom{quantifierSpec}:{string.Join("|", options)}>";
        }
        

        /// <summary>
        /// Finds the index of a custom separator $$ that is not inside nested structures.
        /// </summary>
        /// <param name="text">The text to search in.</param>
        /// <returns>The index of the custom separator, or -1 if not found.</returns>
        private int FindCustomSeparatorIndex(string text)
        {
            int braceDepth = 0;
            int angleDepth = 0;
            
            for (int i = 0; i < text.Length - 1; i++)
            {
                char c = text[i];
                
                // Track nesting depth
                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '<') angleDepth++;
                else if (c == '>') angleDepth--;
                
                // Check for $$ only when we're not inside nested structures
                if (c == '$' && text[i + 1] == '$' && braceDepth == 0 && angleDepth == 0)
                {
                    return i;
                }
            }
            
            return -1; // No custom separator found
        }

        /// <summary>
        /// Checks if the options part contains only a single wildcard (no | separators at the top level).
        /// A single wildcard can be simple like <wildcard:name> or complex like <wildcard:<random[1,]:cat|dog>s>.
        /// </summary>
        /// <param name="optionsPart">The options part to check.</param>
        /// <returns>True if it's a single wildcard, false otherwise.</returns>
        private bool IsSingleWildcard(string optionsPart)
        {
            if (string.IsNullOrEmpty(optionsPart))
            {
                return false;
            }
            
            string trimmed = optionsPart.Trim();
            
            // Must start with <wcwildcard: and end with >
            if (!trimmed.StartsWith("<wcwildcard:") || !trimmed.EndsWith(">"))
            {
                return false;
            }
            
            // Check if there are any | separators at the top level (not inside nested structures)
            return !HasTopLevelPipeSeparators(trimmed);
        }

        /// <summary>
        /// Checks if a string has pipe separators at the top level (not inside nested structures).
        /// </summary>
        /// <param name="text">The text to check.</param>
        /// <returns>True if there are top-level pipe separators, false otherwise.</returns>
        private bool HasTopLevelPipeSeparators(string text)
        {
            int braceDepth = 0;
            int angleDepth = 0;
            
            foreach (char c in text)
            {
                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '<') angleDepth++;
                else if (c == '>') angleDepth--;
                else if (c == '|' && braceDepth == 0 && angleDepth == 0)
                {
                    return true; // Found a top-level pipe separator
                }
            }
            
            return false;
        }

        /// <summary>
        /// Extracts the wildcard name from a processed wildcard pattern (removes the <wcwildcard: and > delimiters).
        /// </summary>
        /// <param name="wildcardOption">The wildcard option like <wcwildcard:flavours> or <wcwildcard:<random[1,]:cat|dog>s>.</param>
        /// <returns>The wildcard name like flavours or <random[1,]:cat|dog>s.</returns>
        private string ExtractWildcardName(string wildcardOption)
        {
            string trimmed = wildcardOption.Trim();
            if (trimmed.StartsWith("<wcwildcard:") && trimmed.EndsWith(">") && trimmed.Length > 13)
            {
                return trimmed.Substring(12, trimmed.Length - 13);
            }
            return wildcardOption; // Fallback, should not happen if IsSingleWildcard returned true
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

        /// <summary>
        /// Processes long-form echo commands in the input line.
        /// Supports: <ppp:echo varname> and <ppp:echo varname>default<ppp:/echo>
        /// Note: Short-form ${varname:default} syntax is handled by ProcessVariables method
        /// </summary>
        /// <param name="line">The input line to process.</param>
        /// <param name="task">The current processing task.</param>
        /// <returns>The processed line with echo commands converted to appropriate directives.</returns>
        private string ProcessEchoCommands(string line, ProcessingTask task)
        {
            // Process long-form echo commands: <ppp:echo varname> and <ppp:echo varname>default<ppp:/echo>
            return ProcessLongFormEchoCommands(line, task);
        }

        /// <summary>
        /// Processes long-form echo commands: <ppp:echo varname> and <ppp:echo varname>default<ppp:/echo>
        /// </summary>
        private string ProcessLongFormEchoCommands(string line, ProcessingTask task)
        {
            // First process <ppp:echo varname>default<ppp:/echo> (with default)
            // Use a more careful pattern that handles nested content properly
            var echoWithDefaultPattern = @"<ppp:echo\s+([^>\s]+?)\s*>([\s\S]*?)<ppp:/echo>";
            line = Regex.Replace(line, echoWithDefaultPattern, match =>
            {
                string varName = match.Groups[1].Value.Trim();
                string defaultValue = match.Groups[2].Value;
                
                // Check for malformed cases (empty variable name)
                if (string.IsNullOrWhiteSpace(varName))
                {
                    return match.Value; // Return original malformed syntax
                }
                
                // If default is empty, just return <macro:varname>
                if (string.IsNullOrEmpty(defaultValue))
                {
                    return $"<macro:{varName}>";
                }
                
                // Process the default value recursively to handle nested syntax
                string processedDefault = ProcessWildcardLine(defaultValue, task.Id);
                
                // Return wcmatch with condition for empty variable and else clause
                return $"<wcmatch:<wccase[length({varName}) eq 0]:{processedDefault}><wccase:<macro:{varName}>>>";
            }, RegexOptions.IgnoreCase);
            
            // Then process <ppp:echo varname> (without default) - only if not already processed
            var echoWithoutDefaultPattern = @"<ppp:echo\s+([^>\s]+?)\s*>(?!.*<ppp:/echo>)";
            line = Regex.Replace(line, echoWithoutDefaultPattern, match =>
            {
                string varName = match.Groups[1].Value.Trim();
                
                // Check for malformed cases (empty variable name)
                if (string.IsNullOrWhiteSpace(varName))
                {
                    return match.Value; // Return original malformed syntax
                }
                
                return $"<macro:{varName}>";
            }, RegexOptions.IgnoreCase);
            
            return line;
        }

        /// <summary>
        /// Processes if commands: <ppp:if condition>content<ppp:elif condition>content<ppp:else>content<ppp:/if>
        /// Converts them to wcmatch/wccase directives with proper expression syntax.
        /// </summary>
        /// <param name="line">The input line to process.</param>
        /// <param name="task">The current processing task.</param>
        /// <returns>The processed line with if commands converted to appropriate directives.</returns>
        private string ProcessIfCommands(string line, ProcessingTask task)
        {
            // Use manual parsing to handle nested if statements properly
            string result = line;
            int startIndex = 0;
            
            while (true)
            {
                // Find the next if statement
                int ifStart = result.IndexOf("<ppp:if ", startIndex, StringComparison.OrdinalIgnoreCase);
                if (ifStart == -1)
                    break;
                    
                // Find the end of the opening tag
                int tagEnd = result.IndexOf('>', ifStart);
                if (tagEnd == -1)
                    break;
                    
                // Extract the condition
                string condition = result.Substring(ifStart + 8, tagEnd - ifStart - 8).Trim();
                
                // Find the matching closing tag using manual counting
                int contentStart = tagEnd + 1;
                int depth = 1;
                int pos = contentStart;
                
                while (pos < result.Length && depth > 0)
                {
                    int nextIf = result.IndexOf("<ppp:if ", pos, StringComparison.OrdinalIgnoreCase);
                    int nextEndIf = result.IndexOf("<ppp:/if>", pos, StringComparison.OrdinalIgnoreCase);
                    
                    if (nextEndIf == -1)
                        break; // Malformed - no closing tag
                        
                    if (nextIf != -1 && nextIf < nextEndIf)
                    {
                        // Found nested if before closing tag
                        depth++;
                        pos = nextIf + 8;
                    }
                    else
                    {
                        // Found closing tag
                        depth--;
                        if (depth == 0)
                        {
                            // This is our matching closing tag
                            string content = result.Substring(contentStart, nextEndIf - contentStart);
                            string fullMatch = result.Substring(ifStart, nextEndIf + 9 - ifStart);
                            
                            try
                            {
                                string replacement = ProcessIfStructure(condition, content, task, fullMatch);
                                result = result.Substring(0, ifStart) + replacement + result.Substring(nextEndIf + 9);
                                startIndex = ifStart + replacement.Length;
                                break;
                            }
                            catch (Exception ex)
                            {
                                task.AddWarning($"Error processing if command: {ex.Message}");
                                startIndex = nextEndIf + 9;
                                break;
                            }
                        }
                        else
                        {
                            pos = nextEndIf + 9;
                        }
                    }
                }
                
                if (depth > 0)
                {
                    // Malformed - no matching closing tag found
                    task.AddWarning("Malformed if command: no matching closing tag");
                    break;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Processes the full if/elif/else structure and converts it to wcmatch/wccase syntax.
        /// </summary>
        private string ProcessIfStructure(string initialCondition, string content, ProcessingTask task, string originalMatch)
        {
            var cases = new List<string>();
            
            // Parse the content to extract elif and else clauses
            var parts = ParseIfContent(content);
            
            // Add the initial if condition
            string processedCondition = ProcessCondition(initialCondition, task);
            if (processedCondition == null)
            {
                // Return original text if condition parsing failed
                return originalMatch;
            }
            
            string processedContent = parts.IfContent;
            if (string.IsNullOrEmpty(processedContent))
            {
                processedContent = "<comment:empty>";
            }
            else
            {
                // Recursively process content to handle nested structures
                processedContent = ProcessWildcardLine(processedContent, task.Id);
            }
            
            cases.Add($"<wccase[{processedCondition}]:{processedContent}>");
            
            // Add elif clauses
            foreach (var elifPart in parts.ElifParts)
            {
                string elifCondition = ProcessCondition(elifPart.Condition, task);
                if (elifCondition != null)
                {
                    string elifContent = string.IsNullOrEmpty(elifPart.Content) ? "<comment:empty>" : ProcessWildcardLine(elifPart.Content, task.Id);
                    cases.Add($"<wccase[{elifCondition}]:{elifContent}>");
                }
            }
            
            // Add else clause if present
            if (parts.ElseContent != null)
            {
                string elseContent = string.IsNullOrEmpty(parts.ElseContent) ? "<comment:empty>" : ProcessWildcardLine(parts.ElseContent, task.Id);
                cases.Add($"<wccase:{elseContent}>");
            }
            
            return $"<wcmatch:{string.Join("", cases)}>";
        }

        /// <summary>
        /// Parses the content of an if block to extract elif and else clauses.
        /// </summary>
        private IfParts ParseIfContent(string content)
        {
            var result = new IfParts();
            var elifParts = new List<ElifPart>();
            
            // Find elif and else tags
            var elifPattern = @"<ppp:elif\s+([^>]+?)>";
            var elsePattern = @"<ppp:else>";
            
            var elifMatches = Regex.Matches(content, elifPattern, RegexOptions.IgnoreCase);
            var elseMatch = Regex.Match(content, elsePattern, RegexOptions.IgnoreCase);
            
            int currentIndex = 0;
            
            // Extract content before first elif or else
            int nextIndex = content.Length;
            if (elifMatches.Count > 0)
                nextIndex = Math.Min(nextIndex, elifMatches[0].Index);
            if (elseMatch.Success)
                nextIndex = Math.Min(nextIndex, elseMatch.Index);
                
            result.IfContent = content.Substring(currentIndex, nextIndex - currentIndex);
            currentIndex = nextIndex;
            
            // Process elif clauses
            for (int i = 0; i < elifMatches.Count; i++)
            {
                var elifMatch = elifMatches[i];
                string condition = elifMatch.Groups[1].Value.Trim();
                
                // Find start of content (after the elif tag)
                int contentStart = elifMatch.Index + elifMatch.Length;
                
                // Find end of content (before next elif or else)
                int contentEnd = content.Length;
                if (i + 1 < elifMatches.Count)
                    contentEnd = Math.Min(contentEnd, elifMatches[i + 1].Index);
                if (elseMatch.Success)
                    contentEnd = Math.Min(contentEnd, elseMatch.Index);
                
                string elifContent = content.Substring(contentStart, contentEnd - contentStart);
                elifParts.Add(new ElifPart { Condition = condition, Content = elifContent });
            }
            
            result.ElifParts = elifParts;
            
            // Process else clause
            if (elseMatch.Success)
            {
                int elseStart = elseMatch.Index + elseMatch.Length;
                result.ElseContent = content.Substring(elseStart);
            }
            
            return result;
        }

        /// <summary>
        /// Processes a condition expression and converts it to wccase syntax.
        /// </summary>
        private string ProcessCondition(string condition, ProcessingTask task)
        {
            condition = condition.Trim();
            
            if (string.IsNullOrWhiteSpace(condition))
                return null;
            
            // Check for simple variable truthiness (no operators)
            if (!Regex.IsMatch(condition, @"\s+(eq|ne|gt|lt|ge|le|contains|in|not)\s+", RegexOptions.IgnoreCase))
            {
                return condition; // Simple variable check
            }
            
            // Parse condition with operators (allow hyphens, underscores in variable names)
            var conditionMatch = Regex.Match(condition, 
                @"^\s*([\w\-]+)\s+(not\s+)?(eq|ne|gt|lt|ge|le|contains|in)\s+(.+?)\s*$", 
                RegexOptions.IgnoreCase);
                
            if (!conditionMatch.Success)
            {
                task.AddWarning($"Could not parse condition: {condition}");
                return null;
            }
            
            string variable = conditionMatch.Groups[1].Value;
            bool isNot = conditionMatch.Groups[2].Success;
            string operation = conditionMatch.Groups[3].Value.ToLowerInvariant();
            string value = conditionMatch.Groups[4].Value.Trim();
            
            // Validate operation
            var validOperations = new[] { "eq", "ne", "gt", "lt", "ge", "le", "contains", "in" };
            if (!validOperations.Contains(operation))
            {
                task.AddWarning($"Could not parse condition: {condition}");
                return null;
            }
            
            // Validate parentheses matching for list operations
            if (value.Contains("(") || value.Contains(")"))
            {
                int openCount = value.Count(c => c == '(');
                int closeCount = value.Count(c => c == ')');
                if (openCount != closeCount)
                {
                    task.AddWarning($"Could not parse condition: {condition}");
                    return null;
                }
            }
            
            // Convert operation to wccase syntax
            string wcaseExpression = ConvertOperationToWcaseExpression(variable, operation, value, isNot, task);
            
            return wcaseExpression;
        }

        /// <summary>
        /// Converts an operation to wccase expression syntax.
        /// </summary>
        private string ConvertOperationToWcaseExpression(string variable, string operation, string value, bool isNot, ProcessingTask task)
        {
            
            // Check if value is a list (parentheses)
            if (value.StartsWith("(") && value.EndsWith(")"))
            {
                string listContent = value.Substring(1, value.Length - 2);
                var values = ParseValueList(listContent);
                
                if (values.Count == 1)
                {
                    // Single value in parentheses, treat as single value
                    return ConvertSingleValueOperation(variable, operation, values[0], isNot);
                }
                else
                {
                    // Multiple values, convert to OR/AND chain
                    return ConvertListOperation(variable, operation, values, isNot);
                }
            }
            else
            {
                // Single value operation
                return ConvertSingleValueOperation(variable, operation, value, isNot);
            }
        }

        /// <summary>
        /// Converts a single value operation to wccase syntax.
        /// </summary>
        private string ConvertSingleValueOperation(string variable, string operation, string value, bool isNot)
        {
            switch (operation.ToLowerInvariant())
            {
                case "eq":
                    return isNot ? $"{variable} ne {value}" : $"{variable} eq {value}";
                case "ne":
                    return isNot ? $"{variable} eq {value}" : $"{variable} ne {value}";
                case "gt":
                    return isNot ? $"{variable} le {value}" : $"{variable} gt {value}";
                case "lt":
                    return isNot ? $"{variable} ge {value}" : $"{variable} lt {value}";
                case "ge":
                    return isNot ? $"{variable} lt {value}" : $"{variable} ge {value}";
                case "le":
                    return isNot ? $"{variable} gt {value}" : $"{variable} le {value}";
                case "contains":
                    return isNot ? $"not contains({variable}, {value})" : $"contains({variable}, {value})";
                case "in":
                    return isNot ? $"not contains({value}, {variable})" : $"contains({value}, {variable})";
                default:
                    // Return as-is for unknown operations
                    return $"{variable} {operation} {value}";
            }
        }

        /// <summary>
        /// Converts a list operation to wccase syntax with OR/AND chains.
        /// </summary>
        private string ConvertListOperation(string variable, string operation, List<string> values, bool isNot)
        {
            var expressions = new List<string>();
            
            foreach (string value in values)
            {
                switch (operation.ToLowerInvariant())
                {
                    case "contains":
                        expressions.Add($"contains({variable}, {value})");
                        break;
                    case "in":
                        expressions.Add($"{variable} eq {value}");
                        break;
                    default:
                        throw new InvalidOperationException($"Operation '{operation}' not supported with lists");
                }
            }
            
            string joinOperator;
            if (isNot)
            {
                // For NOT operations, we need AND (De Morgan's law: not (A or B) = (not A) and (not B))
                if (operation.ToLowerInvariant() == "contains")
                {
                    expressions = expressions.Select(expr => $"not {expr}").ToList();
                    joinOperator = " && ";
                }
                else // in
                {
                    expressions = expressions.Select(expr => expr.Replace(" eq ", " ne ")).ToList();
                    joinOperator = " && ";
                }
            }
            else
            {
                // For normal operations, we use OR
                joinOperator = " || ";
            }
            
            return string.Join(joinOperator, expressions);
        }

        /// <summary>
        /// Parses a comma-separated list of values, preserving quotes and handling escaping.
        /// </summary>
        private List<string> ParseValueList(string listContent)
        {
            var values = new List<string>();
            var currentValue = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '"';
            
            for (int i = 0; i < listContent.Length; i++)
            {
                char c = listContent[i];
                
                if (!inQuotes && (c == '"' || c == '\''))
                {
                    inQuotes = true;
                    quoteChar = c;
                    currentValue.Append(c);
                }
                else if (inQuotes && c == quoteChar)
                {
                    inQuotes = false;
                    currentValue.Append(c);
                }
                else if (!inQuotes && c == ',')
                {
                    values.Add(currentValue.ToString().Trim());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }
            
            // Add the last value
            if (currentValue.Length > 0)
            {
                values.Add(currentValue.ToString().Trim());
            }
            
            return values;
        }

        /// <summary>
        /// Helper class to hold parsed if structure parts.
        /// </summary>
        private class IfParts
        {
            public string IfContent { get; set; }
            public List<ElifPart> ElifParts { get; set; } = new List<ElifPart>();
            public string ElseContent { get; set; }
        }

        /// <summary>
        /// Helper class to hold elif clause information.
        /// </summary>
        private class ElifPart
        {
            public string Condition { get; set; }
            public string Content { get; set; }
        }

        #region STN Command Processing

        /// <summary>
        /// Processes STN (Send To Negative) commands and converts them to wcnegative directives.
        /// Handles position arguments (s/e/pN) and insertion point markers (iN).
        /// </summary>
        /// <param name="line">The input line to process.</param>
        /// <param name="task">The current processing task.</param>
        /// <returns>The processed line with STN commands converted to wcnegative directives.</returns>
        private string ProcessStnCommands(string line, ProcessingTask task)
        {
            string result = line;
            
            // First, handle insertion point markers (iN) - remove them with warnings
            result = ProcessStnInsertionMarkers(result, task);
            
            // Then process STN commands with manual parsing to handle nested structures
            int startIndex = 0;
            
            while (true)
            {
                // Find the next STN command
                int stnStart = result.IndexOf("<ppp:stn", startIndex, StringComparison.OrdinalIgnoreCase);
                if (stnStart == -1)
                    break;
                    
                // Find the end of the opening tag
                int tagEnd = result.IndexOf('>', stnStart);
                if (tagEnd == -1)
                    break;
                    
                // Extract the position argument (if any)
                string positionArg = "";
                int spaceIndex = result.IndexOf(' ', stnStart);
                if (spaceIndex != -1 && spaceIndex < tagEnd)
                {
                    positionArg = result.Substring(spaceIndex + 1, tagEnd - spaceIndex - 1).Trim();
                }
                
                // Find the matching closing tag using manual counting
                int contentStart = tagEnd + 1;
                int depth = 1;
                int pos = contentStart;
                
                while (pos < result.Length && depth > 0)
                {
                    int nextStn = result.IndexOf("<ppp:stn", pos, StringComparison.OrdinalIgnoreCase);
                    int nextEndStn = result.IndexOf("<ppp:/stn>", pos, StringComparison.OrdinalIgnoreCase);
                    
                    if (nextEndStn == -1)
                        break; // Malformed - no closing tag
                        
                    if (nextStn != -1 && nextStn < nextEndStn)
                    {
                        // Found nested STN before closing tag
                        depth++;
                        pos = nextStn + 8;
                    }
                    else
                    {
                        // Found closing tag
                        depth--;
                        if (depth == 0)
                        {
                            // This is our matching closing tag
                            string content = result.Substring(contentStart, nextEndStn - contentStart);
                            string fullMatch = result.Substring(stnStart, nextEndStn + 10 - stnStart);
                            
                            try
                            {
                                string replacement = ProcessStnStructure(positionArg, content, task);
                                result = result.Substring(0, stnStart) + replacement + result.Substring(nextEndStn + 10);
                                startIndex = stnStart + replacement.Length;
                                break;
                            }
                            catch (Exception ex)
                            {
                                task.AddWarning($"Error processing STN command: {ex.Message}");
                                startIndex = nextEndStn + 10;
                                break;
                            }
                        }
                        else
                        {
                            pos = nextEndStn + 10;
                        }
                    }
                }
                
                if (depth > 0)
                {
                    // Malformed - no matching closing tag found
                    task.AddWarning("Malformed STN command: no matching closing tag");
                    break;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Processes STN insertion point markers (iN) and removes them with warnings.
        /// </summary>
        private string ProcessStnInsertionMarkers(string line, ProcessingTask task)
        {
            // Pattern to match insertion point markers like <ppp:stn i0>, <ppp:stn i1>, etc.
            var regex = new Regex(@"<ppp:stn\s+i\d+\s*>", RegexOptions.IgnoreCase);
            
            return regex.Replace(line, match =>
            {
                task.AddWarning($"STN insertion point marker '{match.Value}' is not supported and has been removed");
                return "";
            });
        }

        /// <summary>
        /// Processes a single STN structure and converts it to wcnegative directive.
        /// </summary>
        private string ProcessStnStructure(string positionArg, string content, ProcessingTask task)
        {
            // Recursively process the content to handle nested commands
            string processedContent = ProcessWildcardLine(content, task.Id);
            
            // Determine the position mode and separator
            bool isPrepend = true; // Default is start (prepend)
            string separator = ", "; // Default separator for prepend (suffix)
            
            if (!string.IsNullOrEmpty(positionArg))
            {
                string pos = positionArg.ToLowerInvariant();
                
                if (pos == "e" || pos == "end")
                {
                    // End position - use append mode
                    isPrepend = false;
                    separator = ", "; // For append, we use prefix
                }
                else if (pos == "s" || pos == "start")
                {
                    // Start position - use prepend mode (already set)
                    isPrepend = true;
                    separator = ", ";
                }
                else if (pos.StartsWith("p") && pos.Length > 1 && char.IsDigit(pos[1]))
                {
                    // Insertion point (pN) - fallback to append with warning
                    task.AddWarning($"STN insertion point '{positionArg}' is not supported by wcnegative, falling back to append mode");
                    isPrepend = false;
                    separator = ", ";
                }
                else if (pos != "")
                {
                    // Invalid position - default to start with warning
                    task.AddWarning($"Invalid STN position '{positionArg}', defaulting to start position");
                    isPrepend = true;
                    separator = ", ";
                }
            }
            
            // Build the wcnegative directive
            if (isPrepend)
            {
                // Prepend mode: content gets comma suffix
                return $"<wcnegative[prepend]:{processedContent}{separator}>";
            }
            else
            {
                // Append mode: content gets comma prefix
                return $"<wcnegative:{separator}{processedContent}>";
            }
        }

        #endregion

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
