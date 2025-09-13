namespace Spoomples.Extensions.WildcardImporter
{
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using FreneticUtilities.FreneticExtensions;
    using SwarmUI.Text2Image;
    using SwarmUI.Utils;

    public static partial class PromptDirectives
    {
        // get a new MagesEngine for each Variables dictionary we see.  This effectively gives us a new engine for each prompt we process
        public static ConditionalWeakTable<T2IPromptHandling.PromptTagContext, MagesEngine> EngineCache = new ();
        
        public static readonly string MatchState_None = "none";
        public static readonly string MatchState_Open = "open";
        public static readonly string MatchState_Closed = "closed";
        public static ConditionalWeakTable<T2IPromptHandling.PromptTagContext, string> CurrentMatchState = new ();
        
        public static ThreadLocal<string> CurrentMatchLength = new (() => "");

        // Static helper methods for Mages function registration
        public static bool ContainsHelper(string str, string sub) => str.Contains(sub);
        public static bool IContainsHelper(string str, string sub) => str.ToLowerFast().Contains(sub.ToLowerFast());


        public static T WithNewMatchContext<T>(T2IPromptHandling.PromptTagContext context, Func<T> func)
        {
            var prev = CurrentMatchState.GetValue(context, _ => MatchState_None);
            CurrentMatchState.AddOrUpdate(context, MatchState_Open);
            try
            {
                return func();
            }
            finally
            {
                CurrentMatchState.AddOrUpdate(context, prev);
            }
        }

        public static T WithNewMatchLengthContext<T>(T2IPromptHandling.PromptTagContext context, Func<T> func)
        {
            var prev = CurrentMatchLength.Value;
            CurrentMatchLength.Value = "";
            try
            {
                return func();
            }
            finally
            {
                CurrentMatchLength.Value = prev;
            }
        }

        public static MagesEngine GetEngine(T2IPromptHandling.PromptTagContext context)
        {
            return EngineCache.GetValue(context, _ =>
            {
                var scope = new PromptTagContextDictionary(context);
                var engine = new MagesEngine(scope);
                
                Func<string, string, bool> contains = ContainsHelper;
                Func<string, string, bool> icontains = IContainsHelper;
                
                engine.SetFunction("contains", contains);
                engine.SetFunction("icontains", icontains);
                return engine;
            });
        }
        
        public static void RegisterPromptDirectives()
        {
            AddNegativePrompt();
            AddVariable();
            AddMacro();
            Match();
            EnhancedRandom();
            EnhancedWildcard();
        }

        public static (int, string) InterpretPredataForRandom(string prefix, string preData, string data, T2IPromptHandling.PromptTagContext context)
        {
            int count = 1;
            string separator = " ";
            if (preData is not null)
            {
                if (preData.Contains(','))
                {
                    (preData, separator) = preData.BeforeAndAfter(',');
                    if (separator == "")
                    {
                        separator = ", ";
                    }
                }
                double? countVal = T2IPromptHandling.InterpretNumber(preData, context);
                if (!countVal.HasValue)
                {
                    Logs.Warning($"Random input '{prefix}[{preData}]:{data}' has invalid predata count (not a number) and will be ignored.");
                    return (0, null);
                }
                count = (int)countVal.Value;
            }
            return (count, separator);
        }

        [GeneratedRegex(@"\bif\s+(?<expr>.*)$")]
        public static partial Regex IfConditionRE();
        
        [GeneratedRegex(@"\((?<labels>[^)]*)\)")]
        public static partial Regex LabelsRE();

        record struct RandomChoice(string Value, double Weight, string ConditionExpression, HashSet<string> Labels)
        {
            public RandomChoice(string rawString) : this(rawString, 1.0, null, new HashSet<string>())
            {
                if (rawString.Contains("::"))
                {
                    var (rawOpts, value) = rawString.BeforeAndAfter("::");
                    Value = value;
                    
                    // parse the options, which looks like:
                    // 13 (label1,label2,label3) if x eq "42"
                    // each component is optional.
                    
                    // Lets first see if there is an if condition expression by searching for "\bif "
                    var opts = rawOpts.Trim();
                    var match = IfConditionRE().Match(opts);
                    if (match.Success)
                    {
                        ConditionExpression = match.Groups["expr"].Value;
                        opts = opts.Substring(0, match.Index).Trim();
                    }
                    
                    // Now lets look for a labels section
                    match = LabelsRE().Match(opts);
                    if (match.Success)
                    {
                        var labels = match.Groups["labels"].Value.SplitFast(',');
                        foreach (var label in labels)
                        {
                            Labels.Add(label.Trim());
                        }
                        opts = opts.Remove(match.Index, match.Length).Trim();
                    }
                    
                    // Now look for a weight
                    if (opts != "")
                    {
                        if (double.TryParse(opts, out double parsedWeight))
                        {
                            Weight = Math.Max(0, parsedWeight);;
                        }
                        else
                        {
                            Logs.Warning($"Random choice options section is malformed: '{rawOpts}'");
                        }
                    }
                }
            }
            
            public bool IsConditionTrue(T2IPromptHandling.PromptTagContext context)
            {
                if (ConditionExpression is null)
                {
                    return true;
                }
                var magesEngine = GetEngine(context);
                var exprResult = magesEngine.Compile(ConditionExpression)();
                return exprResult is true or string { Length: > 0 } or double and > 0;
            }
        }

        record ChoiceLabelFilterEntry(int Position, List<string> PositiveLabels, List<string> NegativeLabels)
        {
            public ChoiceLabelFilterEntry(string rawString) : this(-1, null, null)
            {
                // will look like one of these:
                // label1
                // 13
                // label2+label3
                // !label4
                // label5+!label2
                
                // try to parse it as an integer
                if (int.TryParse(rawString, out int position))
                {
                    // convert from 1-based to 0-based
                    Position = position - 1;
                    return;
                }
                
                // try to parse it as a +-separated
                foreach (var rawLabel in rawString.SplitFast('+'))
                {
                    var trimmedRawLabel = rawLabel.Trim();
                    if (trimmedRawLabel.StartsWith('!'))
                    {
                        if (NegativeLabels is null)
                        {
                            NegativeLabels = new List<string>();
                        }
                        NegativeLabels.Add(trimmedRawLabel.Substring(1).Trim());
                    }
                    else
                    {
                        if (PositiveLabels is null)
                        {
                            PositiveLabels = new List<string>();
                        }
                        PositiveLabels.Add(trimmedRawLabel);
                    }
                }
            }

            public bool IsMatch(RandomChoice choice, int position)
            {
                if (Position >= 0)
                {
                    return Position == position;
                }
                
                if (!(PositiveLabels?.All(label => choice.Labels.Contains(label)) ?? true))
                {
                    return false;
                }
                if (NegativeLabels?.Any(label => choice.Labels.Contains(label)) ?? false)
                {
                    return false;
                }
                
                return true;
            }
        }

        record ChoiceLabelFilter(List<ChoiceLabelFilterEntry> Entries)
        {
            public static ChoiceLabelFilter Empty => new(new List<ChoiceLabelFilterEntry>());
            public ChoiceLabelFilter(string rawString) : this(new List<ChoiceLabelFilterEntry>())
            {
                // string should look like: label1,13,label2+label3,!label4,label5+!label2
                foreach (var rawEntry in rawString.SplitFast(','))
                {
                    Entries.Add(new ChoiceLabelFilterEntry(rawEntry.Trim()));
                }
            }

            public bool IsMatch(RandomChoice choice, int position)
            {
                return Entries.IsEmpty() || Entries.Exists(entry => entry.IsMatch(choice, position));
            }
        }

        record RandomChoicesSet(List<RandomChoice> Choices)
        {
            public double TotalWeight { get; set; }
            
            public RandomChoicesSet(string[] rawVals, T2IPromptHandling.PromptTagContext context, ChoiceLabelFilter filter, HashSet<string> exclude = null) : this(new List<RandomChoice>(rawVals.Length))
            {
                TotalWeight = 0;
                int position = 0;
                foreach (string rawString in rawVals)
                {
                    var choice = new RandomChoice(rawString);
                    if (choice.Weight > 0 && !(exclude?.Contains(choice.Value) ?? false) && filter.IsMatch(choice, position) && choice.IsConditionTrue(context))
                    {
                        Choices.Add(choice);
                        TotalWeight += choice.Weight;
                    }
                    ++position;
                }
            }

            public string TakeRandom(T2IPromptHandling.PromptTagContext context)
            {
                if (context.Input.Get(T2IParamTypes.WildcardSeedBehavior, "Random") == "Index")
                {
                    int index = context.Input.GetWildcardSeed() % Choices.Count;
                    var choice = Choices[index];
                    Choices.RemoveAt(index);
                    TotalWeight -= choice.Weight;
                    return choice.Value;
                }

                double random = context.Input.GetWildcardRandom().NextDouble() * TotalWeight;
                int i = 0;
                int stop = Choices.Count - 1;
                while (i < stop && random >= Choices[i].Weight)
                {
                    random -= Choices[i].Weight;
                    i++;
                }
                var c = Choices[i];
                Choices.RemoveAt(i);
                TotalWeight -= c.Weight;
                return c.Value;
            }
        }

        private static void EnhancedRandom()
        {
            T2IPromptHandling.PromptTagProcessors["wcrandom"] = (data, context) =>
            {
                (int count, string partSeparator) = InterpretPredataForRandom("wcrandom", context.PreData, data, context);
                if (partSeparator is null)
                {
                    return null;
                }
                string[] rawVals = T2IPromptHandling.SplitSmart(data);
                if (rawVals.Length == 0)
                {
                    context.TrackWarning($"Random input '{data}' is empty and will be ignored.");
                    return null;
                }
                string result = "";
                var set = new RandomChoicesSet(rawVals, context, ChoiceLabelFilter.Empty);
                if (set.Choices.Count == 0)
                {
                    return result;
                }
                
                var origSet = set with { Choices = [..set.Choices] };
               
                for (int i = 0; i < count; i++)
                {
                    string choice = set.TakeRandom(context);
                    if (result != "")
                    {
                        result += partSeparator;
                    }
                    result += context.Parse(choice).Trim();
                    if (set.Choices.Count == 0)
                    {
                        set.Choices.AddRange(origSet.Choices);
                        set.TotalWeight = origSet.TotalWeight;
                    }
                }
                return result.Trim();
            };
            T2IPromptHandling.PromptTagLengthEstimators["wcrandom"] = (data, context) =>
            {
                string[] rawVals = T2IPromptHandling.SplitSmart(data);
                int longest = 0;
                string longestStr = "";
                foreach (string val in rawVals)
                {
                    string interp = T2IPromptHandling.ProcessPromptLikeForLength(new RandomChoice(val).Value);
                    if (interp.Length > longest)
                    {
                        longest = interp.Length;
                        longestStr = interp;
                    }
                }
                return longestStr;
            };
        }

        private static void AddNegativePrompt()
        {
            /*
               <wcnegative:append this to negative prompt>
               <wcnegative[prepend]:prepend this to negative prompt>
             */
            T2IPromptHandling.PromptTagProcessors["wcnegative"] = (data, context) =>
            {
                var current = context.Input.Get(T2IParamTypes.NegativePrompt) ?? "";
                var updated = context.PreData?.ToLowerFast() == "prepend" ? $"{data}{current}" : $"{current}{data}";
                context.Input.Set(T2IParamTypes.NegativePrompt, updated);
                return "";
            };

            T2IPromptHandling.PromptTagLengthEstimators["wcnegative"] = (data, context) => "";
        }

        private static void AddVariable()
        {
            /*
                <wcaddvar[name]:append this value to var name>
                <wcaddvar[name,prepend]:prepend this value to var name>
             */
            T2IPromptHandling.PromptTagProcessors["wcaddvar"] = (data, context) =>
            {
                string mode = "append";
                string name = context.PreData?.BeforeAndAfter(',', out mode);
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.TrackWarning($"A variable name is required when using wcaddvar.");
                    return null;
                }
                
                data = context.Parse(data);
                var currentValue = context.Variables.GetValueOrDefault(name, "");
                context.Variables[name] = mode?.ToLowerFast() == "prepend" ? $"{data}{currentValue}" : $"{currentValue}{data}";
                return "";
            };

            T2IPromptHandling.PromptTagLengthEstimators["wcaddvar"] = (data, context) => "";
        }

        private static void AddMacro()
        {
            /*
               <wcaddmacro[name]:append this value to macro name>
               <wcaddmacro[name,prepend]:prepend this value to macro name>
             */
            T2IPromptHandling.PromptTagProcessors["wcaddmacro"] = (data, context) =>
            {
                string mode = "append";
                string name = context.PreData?.BeforeAndAfter(',', out mode);
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.TrackWarning($"A macro name is required when using wcaddmacro.");
                    return null;
                }
                var currentValue = context.Macros.GetValueOrDefault(name, "");
                context.Macros[name] = mode?.ToLowerFast() == "prepend" ? $"{data}{currentValue}" : $"{currentValue}{data}";
                return "";
            };
            T2IPromptHandling.PromptTagLengthEstimators["wcaddmacro"] = (data, context) => "";
        }

        private static void Match()
        {
            /*
               <wcmatch:<wccase[myvar == "foo"]:use this value><wccase[myvar == "bar" || myvar.Contains("baz")]:use this value><wccase:default case if nothing else matches>>
             */
            T2IPromptHandling.PromptTagProcessors["wcmatch"] = (data, context) => WithNewMatchContext(context, () => context.Parse(data));
            T2IPromptHandling.PromptTagLengthEstimators["wcmatch"] = (data, context) => WithNewMatchLengthContext(context, () => T2IPromptHandling.ProcessPromptLikeForLength(data));

            T2IPromptHandling.PromptTagProcessors["wccase"] = (data, context) =>
            {
                var currentMatchState = CurrentMatchState.GetValue(context, _ => MatchState_None);
                if (currentMatchState == MatchState_None)
                {
                    context.TrackWarning($"A wccase tag must be inside a wcmatch tag.");
                    return null;
                }
                if (currentMatchState == MatchState_Closed)
                {
                    return "";
                }
                var expr = context.PreData;
                if (string.IsNullOrWhiteSpace(expr))
                {
                    // this is the default case.
                    CurrentMatchState.AddOrUpdate(context, MatchState_Closed);
                    return context.Parse(data);
                }
                try
                {
                    // parse the expression and see if it is truthy
                    var magesEngine = GetEngine(context);
                    var exprResult = magesEngine.Compile(expr)();
                    var isMatch = exprResult is true or string { Length: > 0 } or double and > 0;
                    if (isMatch)
                    {
                        // this case matches the condition, so use it and mark the match as closed.
                        CurrentMatchState.AddOrUpdate(context, MatchState_Closed);
                        return context.Parse(data.Trim());
                    }
                    // this case does not match, so just return empty string.
                    return "";
                }
                catch (Exception ex)
                {
                    context.TrackWarning($"Error evaluating wccase expression '{expr}': {ex.Message}");
                    return null;
                }
            };
            
            T2IPromptHandling.PromptTagLengthEstimators["wccase"] = (data, context) => {
                var lengthOfThisCase = T2IPromptHandling.ProcessPromptLikeForLength(data);
                var currentMatchLength = CurrentMatchLength.Value;
                if (lengthOfThisCase.Length > currentMatchLength.Length)
                {
                    CurrentMatchLength.Value = lengthOfThisCase;
                    return lengthOfThisCase.Substring(currentMatchLength.Length);
                }
                return "";
            };
        }

        private static void EnhancedWildcard()
        {
            T2IPromptHandling.PromptTagProcessors["wcwildcard"] = (data, context) =>
            {
                data = context.Parse(data);
                (data, var labelFilter) = data.BeforeAndAfter(':');
                var choiceLabelFilter = new ChoiceLabelFilter(labelFilter);
                string[] dataParts = data.SplitFast(',', 1);
                data = dataParts[0];
                HashSet<string> exclude = [];
                if (dataParts.Length > 1 && dataParts[1].StartsWithFast("not="))
                {
                    exclude.UnionWith(T2IPromptHandling.SplitSmart(dataParts[1].After('=')));
                }
                (int count, string partSeparator) = InterpretPredataForRandom("wcwildcard", context.PreData, data, context);
                if (partSeparator is null)
                {
                    return null;
                }
                string card = T2IParamTypes.GetBestInList(data, WildcardsHelper.ListFiles);
                if (card is null)
                {
                    context.TrackWarning($"Wildcard input '{data}' does not match any wildcard file and will be ignored.");
                    return null;
                }
                if (data.Length < card.Length)
                {
                    Logs.Warning($"Wildcard input '{data}' is not a valid wildcard name, but appears to match '{card}', will use that instead.");
                }
                WildcardsHelper.Wildcard wildcard = WildcardsHelper.GetWildcard(card);
                List<string> usedWildcards = context.Input.ExtraMeta.GetOrCreate("used_wildcards", () => new List<string>()) as List<string>;
                usedWildcards.Add(card);
                var set = new RandomChoicesSet(wildcard.Options, context, choiceLabelFilter, exclude);
                if (set.Choices.Count == 0)
                {
                    return "";
                }
                
                var origSet = set with { Choices = [..set.Choices] };
                string result = "";
                for (int i = 0; i < count; i++)
                {
                    string choice = set.TakeRandom(context);
                    if (result != "")
                    {
                        result += partSeparator;
                    }
                    result += context.Parse(choice).Trim();
                    if (set.Choices.Count == 0)
                    {
                        set.Choices.AddRange(origSet.Choices);
                        set.TotalWeight = origSet.TotalWeight;
                    }
                }
                return result.Trim();
            };
            T2IPromptHandling.PromptTagLengthEstimators["wcwildcard"] = (data, context) =>
            {
                string card = T2IParamTypes.GetBestInList(data.Before(','), WildcardsHelper.ListFiles);
                if (card is null)
                {
                    return "";
                }
                WildcardsHelper.Wildcard wildcard = WildcardsHelper.GetWildcard(card);
                if (wildcard.MaxLength is not null)
                {
                    return wildcard.MaxLength;
                }
                wildcard.MaxLength = ""; // Recursion protection.
                int longest = 0;
                string longestStr = "";
                foreach (string val in wildcard.Options)
                {
                    string interp = T2IPromptHandling.ProcessPromptLikeForLength(val);
                    if (interp.Length > longest)
                    {
                        longest = interp.Length;
                        longestStr = interp;
                    }
                }
                wildcard.MaxLength = longestStr;
                return longestStr;
            };
        }
    }
}