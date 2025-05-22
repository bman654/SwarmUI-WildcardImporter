namespace Spoomples.Extensions.WildcardImporter
{
    using System.IO;
    using Newtonsoft.Json;
    using System.Reflection;
    using System;
    using System.Linq;

    public class YamlParser
    {
        private readonly Lazy<Func<string, Dictionary<string, object>>> _yamlDeserializer;

        private static Func<string, Dictionary<string, object>> LoadYamlDeserializer(string extensionFolder)
        {
            // Since SwarmUI does not support Extensions declaring dependencies in any way, we have to load YamlDotNet
            // dynamically and create a function bound to its deserializer.
            var assemblyPath = Path.GetFullPath(Path.Combine(extensionFolder, "BundledDeps", "YamlDotNet.dll"));
            if (!File.Exists(assemblyPath))
            {
                throw new Exception("Could not find YamlDotNet.dll");
            }

            var assembly = Assembly.LoadFile(assemblyPath);
            var deserializerType = assembly.GetType("YamlDotNet.Serialization.Deserializer");
            
            // Look for the generic Deserialize<T> method that takes a string parameter
            var methods = deserializerType?.GetMethods().Where(m => 
                m.Name == "Deserialize" && 
                m.IsGenericMethod &&
                m.GetParameters().Length == 1 && 
                m.GetParameters()[0].ParameterType == typeof(string)).ToArray();
                
            if (methods == null || methods.Length == 0)
            {
                throw new Exception("Could not find appropriate YamlDotNet.Deserialize<T> method");
            }
            
            // Use the first matching method
            var deserializeMethod = methods[0];

            // Create an instance of the Deserializer
            var deserializer = Activator.CreateInstance(deserializerType);
            var genericDeserializeMethod = deserializeMethod.MakeGenericMethod(typeof(Dictionary<string, object>));
            
            // Return a function that will use the deserializer to deserialize YAML content
            return yamlContent => (Dictionary<string, object>)genericDeserializeMethod.Invoke(deserializer, new object[] { yamlContent }); 
        }

        public YamlParser(string extensionFolder)
        {
            _yamlDeserializer = new Lazy<Func<string, Dictionary<string, object>>>(() => LoadYamlDeserializer(extensionFolder));
        }
        
        public Dictionary<string, object> Parse(string yamlContent)
        {
            return _yamlDeserializer.Value(yamlContent); 
        }

        public static string SerializeObject(object obj)
        {
            try
            {
                return JsonConvert.SerializeObject(obj, Formatting.Indented);
            }
            catch
            {
                return obj.ToString();
            }
        }
    }
}
