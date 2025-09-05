namespace Spoomples.Extensions.WildcardImporter
{
    using System.Runtime.CompilerServices;
    using FreneticUtilities.FreneticExtensions;
    using SwarmUI.Text2Image;
    using SwarmUI.Utils;

    public static class PromptDirectives
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

        record struct WeightedChoice(string Value, double Weight)
        {
            public WeightedChoice(string rawString) : this(rawString, 1.0)
            {
                int colonIndex = rawString.IndexOf("::", StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    string weightPart = rawString.Substring(0, colonIndex);
                    string valuePart = rawString.Substring(colonIndex + 2);

                    // Try to parse the weight part as a positive number
                    if (double.TryParse(weightPart, out double parsedWeight))
                    {
                        Value = valuePart;
                        Weight = Math.Max(0, parsedWeight);
                        return;
                    }
                }
            }
        }

        record WeightedSet(List<WeightedChoice> Choices)
        {
            public double TotalWeight { get; set; }

            public WeightedSet(string[] rawVals) : this(new List<WeightedChoice>(rawVals.Length))
            {
                TotalWeight = 0;
                foreach (string rawString in rawVals)
                {
                    var choice = new WeightedChoice(rawString);
                    Choices.Add(choice);
                    TotalWeight += choice.Weight;
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
            /*
               use " " as the separator:
               <wcrandom[1-3]:a|0.3::b|6::c|d>
               
               use ", " as the separator:
               <wcrandom[1-3,]:a|0.3::b|6::c|d>
               
               use "custom-separator" as the separator:
               <wcrandom[1-3,custom-separator]:a|0.3::b|6::c|d>
             */
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
                var set = new WeightedSet(rawVals);
                for (int i = 0; i < count; i++)
                {
                    string choice = set.TakeRandom(context);
                    if (result != "")
                    {
                        result += partSeparator;
                    }
                    result += context.Parse(choice).Trim();
                    if (set.Choices.Count == 0 || set.TotalWeight < 0.01)
                    {
                        set = new WeightedSet(rawVals);
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
                    string interp = T2IPromptHandling.ProcessPromptLikeForLength(new WeightedChoice(val).Value);
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
            /*
               Enhanced wildcard directive that uses the new InterpretPredataForRandom method
               with support for custom separators:
               <wcwildcard:cardname>
               <wcwildcard[2]:cardname>
               <wcwildcard[1-3]:cardname>
               <wcwildcard[2,]:cardname> // separator is ", "
               <wcwildcard[1-3, and ]:cardname> // separator is " and "
               <wcwildcard:cardname,not=option1|option2> // exclude specific options
             */
            T2IPromptHandling.PromptTagProcessors["wcwildcard"] = (data, context) =>
            {
                data = context.Parse(data);
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
                string[] options = wildcard.Options;
                if (exclude.Count > 0)
                {
                    options = [.. options.Except(exclude)];
                }
                if (options.Length == 0)
                {
                    return "";
                }
                List<string> vals = [.. options];
                string result = "";
                for (int i = 0; i < count; i++)
                {
                    int index;
                    if (context.Input.Get(T2IParamTypes.WildcardSeedBehavior, "Random") == "Index")
                    {
                        index = context.Input.GetWildcardSeed() % vals.Count;
                    }
                    else
                    {
                        index = context.Input.GetWildcardRandom().Next(vals.Count);
                    }
                    string choice = vals[index];
                    if (result != "")
                    {
                        result += partSeparator;
                    }
                    result += context.Parse(choice).Trim();
                    if (vals.Count == 1)
                    {
                        vals = [.. options];
                    }
                    else
                    {
                        vals.RemoveAt(index);
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