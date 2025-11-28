namespace Spoomples.Extensions.WildcardImporter
{
    using System.Collections;
    using SwarmUI.Text2Image;

    public class PromptTagContextDictionary : IDictionary<String, Object>
    {
        private readonly T2IPromptHandling.PromptTagContext _context;
        private readonly Dictionary<string, object> _extra = new();
        private static readonly HashSet<string> _forbiddenSymbols = new() { "any", "contains", "icontains", "length" };

        public PromptTagContextDictionary(T2IPromptHandling.PromptTagContext context)
        {
            _context = context;
        }

        ICollection<string> IDictionary<string, object>.Keys => throw new NotImplementedException();

        ICollection<object> IDictionary<string, object>.Values => throw new NotImplementedException();

        int ICollection<KeyValuePair<string, object>>.Count => throw new NotImplementedException();

        bool ICollection<KeyValuePair<string, object>>.IsReadOnly => throw new NotImplementedException();

        object IDictionary<string, object>.this[string key]
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        void IDictionary<string, object>.Add(string key, object value)
        {
            _extra.Add(key, value);
        }

        bool IDictionary<string, object>.ContainsKey(string key)
        {
            // return _context.Macros.ContainsKey(key) || _context.Variables.ContainsKey(key) || _extra.ContainsKey(key);
            // we will return empty string instead of null for missing variables so from Mages perspective the key always exists
            return !_forbiddenSymbols.Contains(key);
        }

        bool IDictionary<string, object>.Remove(string key)
        {
            return _extra.Remove(key);
        }

        bool IDictionary<string, object>.TryGetValue(string key, out object value)
        {
            if (_context.Macros.TryGetValue(key, out var macro))
            {
                value = _context.Parse(macro);
                return true;
            }
            if (_context.Variables.TryGetValue(key, out var variable))
            {
                value = variable;
                return true;
            }

            if (_extra.TryGetValue(key, out value))
            {
                return true;
            }

            if (!_forbiddenSymbols.Contains(key))
            {
                // return empty string
                value = "";
                return true;
            }
            return false;
        }

        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item)
        {
            _extra.Add(item.Key, item.Value);
        }

        void ICollection<KeyValuePair<string, object>>.Clear()
        {
            _extra.Clear();
        }

        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item)
        {
            return _extra.Remove(item.Key);
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}


