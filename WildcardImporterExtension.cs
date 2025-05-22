using SwarmUI.Core;
using SwarmUI.Utils;

namespace Spoomples.Extensions.WildcardImporter
{
    public class WildcardImporterExtension : Extension
    {
        private WildcardImporterAPI _api = null;
        
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
