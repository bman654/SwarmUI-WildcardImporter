namespace Spoomples.Extensions.WildcardImporter
{
    using System.Runtime.CompilerServices;
    using FreneticUtilities.FreneticExtensions;
    using SwarmUI.Text2Image;

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
                var updated = context.PreData.ToLowerFast() == "prepend" ? $"{data}{current}" : $"{current}{data}";
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
                string name = context.PreData.BeforeAndAfter(',', out string mode);
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.TrackWarning($"A variable name is required when using wcaddvar.");
                    return null;
                }
                
                data = context.Parse(data);
                var currentValue = context.Variables.GetValueOrDefault(name, "");
                context.Variables[name] = mode.ToLowerFast() == "prepend" ? $"{data}{currentValue}" : $"{currentValue}{data}";
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
                string name = context.PreData.BeforeAndAfter(',', out string mode);
                if (string.IsNullOrWhiteSpace(name))
                {
                    context.TrackWarning($"A macro name is required when using wcaddmacro.");
                    return null;
                }
                var currentValue = context.Macros.GetValueOrDefault(name, "");
                context.Macros[name] = mode.ToLowerFast() == "prepend" ? $"{data}{currentValue}" : $"{currentValue}{data}";
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
    }
}