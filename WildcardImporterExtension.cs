using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    using SwarmUI.Accounts;
    using SwarmUI.Text2Image;

    public class WildcardImporterExtension : Extension
    {
        private WildcardImporterAPI _api = null;

        private T2IRegisteredParam<bool> PromptCleanup;

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

            T2IEngine.PreGenerateEvent += @params =>
            {
                if (@params.UserInput.InternalSet.Get(PromptCleanup))
                {
                    var posPrompt = @params.UserInput.InternalSet.Get(T2IParamTypes.Prompt);
                    if (posPrompt != null)
                    {
                        @params.UserInput.InternalSet.Set(T2IParamTypes.Prompt, Clean(posPrompt));
                    }

                    var negPrompt = @params.UserInput.InternalSet.Get(T2IParamTypes.NegativePrompt);
                    if (negPrompt != null)
                    {
                        @params.UserInput.InternalSet.Set(T2IParamTypes.NegativePrompt, Clean(negPrompt));
                    }
                }
            };
        }

        private string Clean(String prompt)
        {
            // Ensure every "," or ")" has a space after it
            prompt = System.Text.RegularExpressions.Regex.Replace(prompt, @"([,)])(?!\s)", "$1 ");

            // Replace 2+ whitespace with single space
            prompt = System.Text.RegularExpressions.Regex.Replace(prompt, @"\s{2,}", " ");

            // Replace 2+ ", " with single ", "
            prompt = System.Text.RegularExpressions.Regex.Replace(prompt, @"(,\s*)+", ", ");

            // Remove any commas or spaces before and after the <break> tag (case-insensitive)
            prompt = System.Text.RegularExpressions.Regex.Replace(prompt, @"[, ]*(?i)<break>[, ]*", "<break>");

            // Remove trailing ", " and " "
            prompt = prompt.TrimEnd(' ', ',');

            return prompt;
        }
    }
}