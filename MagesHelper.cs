using System;
using System.Collections.Generic;
using Mages.Core;

namespace Spoomples.Extensions.WildcardImporter
{
    /// <summary>
    /// Helper class wrapping the Mages.Core expression engine.
    /// Provides a clean wrapper around the Mages expression engine.
    /// </summary>
    public class MagesEngine : IDisposable
    {
        private readonly Engine _engine;

        /// <summary>
        /// Creates a new Mages engine instance
        /// </summary>
        /// <param name="scope">Optional scope dictionary to initialize the engine with</param>
        public MagesEngine(IDictionary<string, object> scope = null)
        {
            if (scope != null)
            {
                var config = new Configuration { Scope = scope };
                _engine = new Engine(config);
            }
            else
            {
                _engine = new Engine();
            }
        }

        /// <summary>
        /// Interprets an expression and returns the result as an object
        /// </summary>
        /// <param name="expression">The expression to evaluate</param>
        /// <returns>The result of the expression</returns>
        public object Interpret(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;

            return _engine.Interpret(expression);
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

            var result = _engine.Interpret(expression);
            return result is T typedResult ? typedResult : default(T);
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
                var compiledFunction = _engine.Compile(convertedExpression);
                _compiledFunctions[convertedExpression] = compiledFunction;
                return compiledFunction;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to parse expression. {ex.Message}: {expression}", ex);
            }
        }

        /// <summary>
        /// Gets the global variables dictionary
        /// </summary>
        public IDictionary<string, object> Globals => _engine.Globals;

        /// <summary>
        /// Gets the current scope dictionary
        /// </summary>
        public IDictionary<string, object> Scope => _engine.Scope;

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

            _engine.SetFunction(name, function);
        }

        /// <summary>
        /// Adds or replaces a function represented as a reflected method info by
        /// wrapping it as a function with the given name.
        /// </summary>
        /// <param name="name">The name of the function to add or replace</param>
        /// <param name="method">The method to be wrapped</param>
        /// <param name="target">The optional target object of the method</param>
        public void SetFunction(string name, System.Reflection.MethodInfo method, object target = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Function name cannot be null or empty", nameof(name));
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            _engine.SetFunction(name, method, target);
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

            EngineExtensions.SetConstant(_engine, name, value);
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
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\band\b", "&&");
            expression = System.Text.RegularExpressions.Regex.Replace(expression, @"\bor\b", "||");

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
