using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    using SwarmUI.Accounts;

    public class WildcardImporterExtension : Extension
    {
        private WildcardImporterAPI _api = null;
        
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
        }
    }
}
