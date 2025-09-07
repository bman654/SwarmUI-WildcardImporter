using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Spoomples.Extensions.WildcardImporter
{
    /// <summary>
    /// Helper class for using Mages.Core via reflection to avoid direct assembly dependencies.
    /// Provides a clean wrapper around the Mages expression engine.
    /// </summary>
    public class MagesEngine : IDisposable
    {
        private static Assembly _magesAssembly;
        private static Type _engineType;
        private static Type _functionType;
        private static Type _configurationType;
        private static bool _initialized = false;
        private static Exception _initializationError;
        private static string _extensionFolder;

        private readonly object _engine;
        private readonly MethodInfo _interpretMethod;
        private readonly MethodInfo _interpretGenericMethod;
        private readonly MethodInfo _compileMethod;
        private readonly PropertyInfo _globalsProperty;
        private readonly PropertyInfo _scopeProperty;

        /// <summary>
        /// Creates a new Mages engine instance
        /// </summary>
        /// <param name="scope">Optional scope dictionary to initialize the engine with</param>
        public MagesEngine(IDictionary<string, object> scope = null)
        {
            if (!IsAvailable)
            {
                var errorMsg = _initializationError?.Message ?? "MagesHelper.Init() has not been called";
                throw new InvalidOperationException($"Mages.Core is not available: {errorMsg}");
            }

            try
            {
                if (scope != null)
                {
                    // Create Configuration with custom scope
                    var config = Activator.CreateInstance(_configurationType);
                    
                    // Set the Scope property
                    var scopeProperty = _configurationType.GetProperty("Scope");
                    scopeProperty?.SetValue(config, scope);
                    
                    // Create engine with configuration
                    var engineConstructor = _engineType.GetConstructor(new[] { _configurationType });
                    _engine = engineConstructor?.Invoke(new[] { config }) ?? Activator.CreateInstance(_engineType);
                }
                else
                {
                    _engine = Activator.CreateInstance(_engineType);
                }
                
                // Cache reflection info for better performance
                _interpretMethod = _engineType.GetMethod("Interpret", new[] { typeof(string) });
                _interpretGenericMethod = _engineType.GetMethod("Interpret", 1, new[] { typeof(string) });
                _compileMethod = _engineType.GetMethod("Compile");
                _globalsProperty = _engineType.GetProperty("Globals");
                _scopeProperty = _engineType.GetProperty("Scope");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create Mages engine instance: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets whether Mages.Core is available and loaded successfully
        /// </summary>
        public static bool IsAvailable => _initialized && _initializationError == null;

        /// <summary>
        /// Gets the initialization error if Mages.Core failed to load
        /// </summary>
        public static Exception InitializationError => _initializationError;

        /// <summary>
        /// Interprets an expression and returns the result as an object
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The result of the expression</returns>
        public object Interpret(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;

            try
            {
                return _interpretMethod?.Invoke(_engine, new object[] { expression });
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"Mages interpretation error: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
        }

        /// <summary>
        /// Interprets an expression and returns the result as the specified type
        /// </summary>
        /// <typeparam name="T">The expected return type</typeparam>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The result of the expression cast to type T</returns>
        public T Interpret<T>(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return default(T);

            try
            {
                // Try to use the generic method if available
                if (_interpretGenericMethod != null)
                {
                    var genericMethod = _interpretGenericMethod.MakeGenericMethod(typeof(T));
                    return (T)genericMethod.Invoke(_engine, new object[] { expression });
                }
                
                // Fallback to regular interpret and cast
                var result = Interpret(expression);
                return result is T typedResult ? typedResult : default(T);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"Mages interpretation error: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
        }
        
        private Dictionary<string, Func<object>> _compiledFunctions = new();

        /// <summary>
        /// Compiles an expression into a reusable function
        /// </summary>
        /// <param name="expression">The expression to compile</param>
        /// <returns>A compiled function that can be invoked multiple times</returns>
        public Func<object> Compile(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;
            
            // Convert alphanumeric operators to symbolic operators for Mage expression language
            var convertedExpression = ConvertAlphanumericOperators(expression);
            
            if (_compiledFunctions.TryGetValue(convertedExpression, out var cachedFunction))
                return cachedFunction;

            try
            {
                var compiledFunction = _compileMethod?.Invoke(_engine, new object[] { convertedExpression });
                if (compiledFunction != null)
                {
                    _compiledFunctions[convertedExpression] = compiledFunction as Func<object>;
                    return _compiledFunctions[convertedExpression];
                }
                return null;
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"Mages compilation error: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
        }

        /// <summary>
        /// Gets the global variables dictionary
        /// </summary>
        public IDictionary<string, object> Globals
        {
            get
            {
                try
                {
                    var globals = _globalsProperty?.GetValue(_engine);
                    return globals as IDictionary<string, object> ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to access Globals: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Gets the current scope dictionary
        /// </summary>
        public IDictionary<string, object> Scope
        {
            get
            {
                try
                {
                    var scope = _scopeProperty?.GetValue(_engine);
                    return scope as IDictionary<string, object> ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to access Scope: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Adds or replaces a function represented as a general delegate by wrapping
        /// it as a function with the given name.
        /// </summary>
        /// <param name="name">The name of the function to add or replace</param>
        /// <param name="function">The function delegate to be wrapped</param>
        public void SetFunction(string name, Delegate function)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Function name cannot be null or empty", nameof(name));
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            try
            {
                // Call the SetFunction extension method directly on the engine
                var setFunctionMethod = GetSetFunctionMethodForDelegate();
                if (setFunctionMethod != null)
                {
                    setFunctionMethod.Invoke(null, new object[] { _engine, name, function });
                }
                else
                {
                    // Fallback: set the delegate directly
                    Globals[name] = function;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set function '{name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Adds or replaces a function represented as a reflected method info by
        /// wrapping it as a function with the given name.
        /// </summary>
        /// <param name="name">The name of the function to add or replace</param>
        /// <param name="method">The method to be wrapped</param>
        /// <param name="target">The optional target object of the method</param>
        public void SetFunction(string name, MethodInfo method, object target = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Function name cannot be null or empty", nameof(name));
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            try
            {
                // Call the SetFunction extension method directly on the engine
                var setFunctionMethod = GetSetFunctionMethodForMethodInfo();
                if (setFunctionMethod != null)
                {
                    setFunctionMethod.Invoke(null, new object[] { _engine, name, method, target });
                }
                else
                {
                    // Fallback: create a delegate from the method
                    var del = target != null ? method.CreateDelegate(typeof(Func<object[], object>), target) 
                                             : method.CreateDelegate(typeof(Func<object[], object>));
                    Globals[name] = del;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set function '{name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Adds or replaces an object represented as the MAGES primitive. This is
        /// either directly the given value or a wrapper around it.
        /// </summary>
        /// <param name="name">The name of the constant to add or replace</param>
        /// <param name="value">The value to interact with</param>
        public void SetConstant(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Constant name cannot be null or empty", nameof(name));

            try
            {
                // Call the WrapObject extension method on the value
                var wrapObjectMethod = GetWrapObjectMethod();
                var wrappedValue = wrapObjectMethod?.Invoke(null, new object[] { value });
                
                if (wrappedValue != null)
                {
                    Globals[name] = wrappedValue;
                }
                else
                {
                    // Fallback: set the value directly
                    Globals[name] = value;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set constant '{name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tries to interpret an expression safely, returning false if it fails
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <param name="result">The result if successful</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryInterpret(string expression, out object result)
        {
            result = null;
            try
            {
                result = Interpret(expression);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to interpret an expression safely with type conversion
        /// </summary>
        /// <typeparam name="T">The expected return type</typeparam>
        /// <param name="expression">The expression to evaluate</param>
        /// <param name="result">The result if successful</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool TryInterpret<T>(string expression, out T result)
        {
            result = default(T);
            try
            {
                result = Interpret<T>(expression);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo _wrapObjectMethod;
        private static MethodInfo _setFunctionMethodForDelegate;
        private static MethodInfo _setFunctionMethodForMethodInfo;
        
        /// <summary>
        /// Gets the WrapObject extension method
        /// </summary>
        private static MethodInfo GetWrapObjectMethod()
        {
            if (_wrapObjectMethod == null && _magesAssembly != null)
            {
                var extensionsType = _magesAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "ObjectExtensions" || t.Name.Contains("Extensions"));
                _wrapObjectMethod = extensionsType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "WrapObject" && 
                                        m.GetParameters().Length == 1 && 
                                        m.GetParameters()[0].ParameterType == typeof(object));
            }
            return _wrapObjectMethod;
        }

        /// <summary>
        /// Gets the SetFunction extension method for Delegate
        /// </summary>
        private static MethodInfo GetSetFunctionMethodForDelegate()
        {
            if (_setFunctionMethodForDelegate == null && _magesAssembly != null)
            {
                // Look for EngineExtensions type
                var extensionsType = _magesAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "EngineExtensions");
                
                if (extensionsType != null)
                {
                    var methods = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    
                    _setFunctionMethodForDelegate = methods.FirstOrDefault(m => 
                        m.Name == "SetFunction" && 
                        m.GetParameters().Length == 3 && 
                        m.GetParameters()[0].ParameterType == _engineType &&
                        m.GetParameters()[1].ParameterType == typeof(string) &&
                        m.GetParameters()[2].ParameterType == typeof(Delegate));
                }
            }
            return _setFunctionMethodForDelegate;
        }

        /// <summary>
        /// Gets the SetFunction extension method for MethodInfo
        /// </summary>
        private static MethodInfo GetSetFunctionMethodForMethodInfo()
        {
            if (_setFunctionMethodForMethodInfo == null && _magesAssembly != null)
            {
                // Look for EngineExtensions type
                var extensionsType = _magesAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "EngineExtensions");
                
                if (extensionsType != null)
                {
                    var methods = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    
                    _setFunctionMethodForMethodInfo = methods.FirstOrDefault(m => 
                        m.Name == "SetFunction" && 
                        m.GetParameters().Length == 4 && 
                        m.GetParameters()[0].ParameterType == _engineType &&
                        m.GetParameters()[1].ParameterType == typeof(string) &&
                        m.GetParameters()[2].ParameterType == typeof(MethodInfo) &&
                        m.GetParameters()[3].ParameterType == typeof(object));
                }
            }
            return _setFunctionMethodForMethodInfo;
        }

        /// <summary>
        /// Initializes the Mages.Core assembly from the extension folder.
        /// Must be called before creating any MagesEngine instances.
        /// </summary>
        /// <param name="extensionFolder">The path to the extension folder containing BundledDeps</param>
        public static void Init(string extensionFolder)
        {
            if (_initialized) return;

            _extensionFolder = extensionFolder;

            try
            {
                // Load Mages.Core assembly from BundledDeps
                var magesPath = Path.GetFullPath(Path.Combine(extensionFolder, "BundledDeps", "Mages.Core.dll"));
                
                if (!File.Exists(magesPath))
                {
                    _initializationError = new FileNotFoundException($"Mages.Core.dll not found at: {magesPath}");
                    return;
                }

                _magesAssembly = Assembly.LoadFile(magesPath);
                _engineType = _magesAssembly.GetType("Mages.Core.Engine");
                _functionType = _magesAssembly.GetType("Mages.Core.Function");
                _configurationType = _magesAssembly.GetType("Mages.Core.Configuration");

                if (_engineType == null)
                {
                    _initializationError = new TypeLoadException("Could not find Mages.Core.Engine type in assembly");
                    return;
                }

                if (_configurationType == null)
                {
                    _initializationError = new TypeLoadException("Could not find Mages.Core.Configuration type in assembly");
                    return;
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _initializationError = ex;
            }
            finally
            {
                _initialized = true; // Mark as initialized even if failed to prevent retries
            }
        }

        /// <summary>
        /// Converts alphanumeric operators to symbolic operators for Mage expression language
        /// </summary>
        /// <param name="expression">Expression with alphanumeric operators</param>
        /// <returns>Expression with symbolic operators</returns>
        private static string ConvertAlphanumericOperators(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return expression;

            // Use word boundaries to ensure we only replace standalone operators, not parts of words
            // Order matters: longer operators first to avoid partial replacements
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\bge\b", ">=");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\ble\b", "<=");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\bne\b", "~=");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\beq\b", "==");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\bgt\b", ">");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\blt\b", "<");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\bnot\b", "~");
            
            return expression;
        }

        public void Dispose()
        {
            // Mages engine doesn't require explicit disposal, but we implement IDisposable for consistency
        }
    }
    
    /// <summary>
    /// Static helper class for quick Mages operations without creating engine instances
    /// </summary>
    public static class MagesHelper
    {
        private static readonly Lazy<MagesEngine> _defaultEngine = new Lazy<MagesEngine>(() => new MagesEngine());

        /// <summary>
        /// Gets whether Mages.Core is available
        /// </summary>
        public static bool IsAvailable => MagesEngine.IsAvailable;

        /// <summary>
        /// Gets the initialization error if any
        /// </summary>
        public static Exception InitializationError => MagesEngine.InitializationError;

        /// <summary>
        /// Quick interpretation using the default engine
        /// </summary>
        /// <param name="expression">Expression to evaluate</param>
        /// <returns>Result of the expression</returns>
        public static object Interpret(string expression)
        {
            return _defaultEngine.Value.Interpret(expression);
        }

        /// <summary>
        /// Quick interpretation with type conversion using the default engine
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="expression">Expression to evaluate</param>
        /// <returns>Result of the expression cast to type T</returns>
        public static T Interpret<T>(string expression)
        {
            return _defaultEngine.Value.Interpret<T>(expression);
        }

        /// <summary>
        /// Quick safe interpretation using the default engine
        /// </summary>
        /// <param name="expression">Expression to evaluate</param>
        /// <param name="result">Result if successful</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool TryInterpret(string expression, out object result)
        {
            return _defaultEngine.Value.TryInterpret(expression, out result);
        }

        /// <summary>
        /// Quick safe interpretation with type conversion using the default engine
        /// </summary>
        /// <typeparam name="T">Expected return type</typeparam>
        /// <param name="expression">Expression to evaluate</param>
        /// <param name="result">Result if successful</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool TryInterpret<T>(string expression, out T result)
        {
            return _defaultEngine.Value.TryInterpret<T>(expression, out result);
        }

        /// <summary>
        /// Gets the default engine's globals dictionary
        /// </summary>
        public static IDictionary<string, object> Globals => _defaultEngine.Value.Globals;

        /// <summary>
        /// Gets the default engine's scope dictionary
        /// </summary>
        public static IDictionary<string, object> Scope => _defaultEngine.Value.Scope;
    }
}
