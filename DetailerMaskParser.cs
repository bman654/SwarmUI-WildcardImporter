namespace Spoomples.Extensions.WildcardImporter;

using System.Globalization;
using System.Text.RegularExpressions;

public class DetailerMaskParser
{
    private readonly string _input;
    private int _position;

    public DetailerMaskParser(string input)
    {
        _input = input;
        _position = 0;
    }

    public MaskSpecifier ParseExpression(char? expected = null)
    {
            
        var left = ParseInvertExpression();
        SkipWhitespace();
        while (!IsAtEnd())
        {
            var op = ConsumeChar();
            switch (op) 
            {
                case '+':
                    left = ParsePostfixExpression(left);
                    break;
                case '|':
                    left = new UnionMask(left, ParseInvertExpression());
                    break;
                case '&':
                    left = new IntersectMask(left, ParseInvertExpression());
                    break;
                default:
                    if (expected == op)
                        return left;
                    throw new InvalidOperationException($"Unexpected character '{op}' at position {_position} in input '{_input}'.");
            }
            SkipWhitespace();
        }
            
        if (expected != null)
            throw new InvalidOperationException($"Expected character '{expected}' at position {_position} in input '{_input}'.");
            
        return left;
    }

    // Precedence level 2: ! (invert)
    private MaskSpecifier ParseInvertExpression()
    {
        SkipWhitespace();
            
        if (!IsAtEnd() && PeekChar() == '!')
        {
            ConsumeChar();
            SkipWhitespace();
            var mask = ParseInvertExpression();
            if (mask is InvertMask)
                return ((InvertMask)mask).Mask;
            return new InvertMask(mask);
        }

        return ParseIndexExpression();
    }

    // Precedence level 1.5: [N] (index operator - postfix)
    private MaskSpecifier ParseIndexExpression()
    {
        var expr = ParsePrimaryExpression();
        
        // Handle postfix index operator
        while (!IsAtEnd() && PeekChar() == '[')
        {
            ConsumeChar(); // consume '['
            SkipWhitespace();
            
            if (!TryParseInteger(out int index))
                throw new InvalidOperationException($"Expected integer index at position {_position}");
            
            SkipWhitespace();
            if (IsAtEnd() || PeekChar() != ']')
                throw new InvalidOperationException($"Expected ']' after index at position {_position}");
            
            ConsumeChar(); // consume ']'
            expr = new IndexedMask(expr, index);
            SkipWhitespace();
        }
        
        return expr;
    }

    // Handle postfix + operator (grow)
    private MaskSpecifier ParsePostfixExpression(MaskSpecifier expr)
    {
        SkipWhitespace();
            
        if (!TryParseInteger(out int pixels))
            throw new InvalidOperationException($"Expected integer after '+' at position {_position}");
            
        expr = new GrowMask(expr, pixels);
        SkipWhitespace();
        return expr;
    }

    // Precedence level 1: Parentheses and primary expressions
    private MaskSpecifier ParsePrimaryExpression()
    {
        SkipWhitespace();

        if (IsAtEnd())
            throw new InvalidOperationException("Unexpected end of input");

        char ch = PeekChar();

        // Parentheses
        if (ch == '(')
        {
            ConsumeChar(); // consume '('
            SkipWhitespace();
                
            // Check if this might be a box() function
            if (_position > 1 && _input.Substring(Math.Max(0, _position - 4), Math.Min(4, _position)).EndsWith("box("))
            {
                // This is actually a box function, backtrack
                _position--;
                return ParseFunction();
            }
                
            var expr = ParseExpression(')');
            SkipWhitespace();
            return expr;
        }

        // Functions (box, bbox)
        if (char.IsLetter(ch))
        {
            return ParseFunction();
        }

        throw new InvalidOperationException($"Unexpected character '{ch}' at position {_position}");
    }

    private MaskSpecifier ParseFunction()
    {
        var functionName = ParseIdentifier();
        
        if (functionName == "box")
        {
            return ParseFunctionCall(functionName);
        }
        else if (functionName.StartsWith("yolo-"))
        {
            // For YOLO masks, we need to capture the class filter part
            var modelName = functionName.Substring(5); // Remove "yolo-" prefix
            var classFilter = "";
            
            // Check for optional (classes) part
            if (!IsAtEnd() && PeekChar() == '(')
            {
                ConsumeChar(); // consume '('
                var classContent = new StringBuilder();
                while (!IsAtEnd() && PeekChar() != ')')
                {
                    classContent.Append(ConsumeChar());
                }
                if (!IsAtEnd() && PeekChar() == ')')
                {
                    ConsumeChar(); // consume ')'
                    classFilter = classContent.ToString();
                }
                else
                {
                    throw new InvalidOperationException($"Expected ')' after class list at position {_position}");
                }
            }
            
            var baseMask = new YoloMask(modelName, classFilter);
            return ParseThresholdSuffix(baseMask);
        }
        else if (functionName == "circle")
        {
            return ParseFunctionCall(functionName);
        }
        else
        {
            // Treat as CLIPSEG mask
            return ParseClipSegMask(functionName);
        }
    }

    // Helper types and methods for clean argument parsing
    private struct Maybe<T>
    {
        public readonly bool HasValue;
        public readonly T Value;

        private Maybe(bool hasValue, T value)
        {
            HasValue = hasValue;
            Value = value;
        }

        public static Maybe<T> Some(T value) => new(true, value);
        public static Maybe<T> None() => new(false, default(T));

        public TResult Fold<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)
        {
            return HasValue ? onSome(Value) : onNone();
        }
    }

    private Maybe<T1> TryParseArgs<T1>(Func<T1> tryParse1)
    {
        var savedPosition = _position;
        try
        {
            var arg1 = tryParse1();
            SkipWhitespace();
            if (!IsAtEnd() && PeekChar() == ')')
            {
                ConsumeChar(); // consume ')'
                return Maybe<T1>.Some(arg1);
            }
        }
        catch
        {
            // Parsing failed
        }
        
        _position = savedPosition;
        return Maybe<T1>.None();
    }

    private Maybe<(T1, T2, T3)> TryParseArgs<T1, T2, T3>(
        Func<T1> tryParse1, 
        Func<T2> tryParse2, 
        Func<T3> tryParse3)
    {
        var savedPosition = _position;
        try
        {
            var arg1 = tryParse1();
            SkipWhitespace();
            if (!IsAtEnd() && PeekChar() == ',')
            {
                ConsumeChar(); // consume ','
                SkipWhitespace();
                
                var arg2 = tryParse2();
                SkipWhitespace();
                if (!IsAtEnd() && PeekChar() == ',')
                {
                    ConsumeChar(); // consume ','
                    SkipWhitespace();
                    
                    var arg3 = tryParse3();
                    SkipWhitespace();
                    if (!IsAtEnd() && PeekChar() == ')')
                    {
                        ConsumeChar(); // consume ')'
                        return Maybe<(T1, T2, T3)>.Some((arg1, arg2, arg3));
                    }
                }
            }
        }
        catch
        {
            // Parsing failed
        }
        
        _position = savedPosition;
        return Maybe<(T1, T2, T3)>.None();
    }

    private Maybe<(T1, T2, T3, T4)> TryParseArgs<T1, T2, T3, T4>(
        Func<T1> tryParse1, 
        Func<T2> tryParse2, 
        Func<T3> tryParse3, 
        Func<T4> tryParse4)
    {
        var savedPosition = _position;
        try
        {
            var arg1 = tryParse1();
            SkipWhitespace();
            if (!IsAtEnd() && PeekChar() == ',')
            {
                ConsumeChar(); // consume ','
                SkipWhitespace();
                
                var arg2 = tryParse2();
                SkipWhitespace();
                if (!IsAtEnd() && PeekChar() == ',')
                {
                    ConsumeChar(); // consume ','
                    SkipWhitespace();
                    
                    var arg3 = tryParse3();
                    SkipWhitespace();
                    if (!IsAtEnd() && PeekChar() == ',')
                    {
                        ConsumeChar(); // consume ','
                        SkipWhitespace();
                        
                        var arg4 = tryParse4();
                        SkipWhitespace();
                        if (!IsAtEnd() && PeekChar() == ')')
                        {
                            ConsumeChar(); // consume ')'
                            return Maybe<(T1, T2, T3, T4)>.Some((arg1, arg2, arg3, arg4));
                        }
                    }
                }
            }
        }
        catch
        {
            // Parsing failed
        }
        
        _position = savedPosition;
        return Maybe<(T1, T2, T3, T4)>.None();
    }

    private double ParseDoubleArg()
    {
        if (!TryParseDouble(out double value))
            throw new InvalidOperationException($"Expected numeric value at position {_position}");
        return value;
    }

    private MaskSpecifier ParseFunctionCall(string functionName)
    {
        SkipWhitespace();
        if (IsAtEnd() || PeekChar() != '(')
            throw new InvalidOperationException($"Expected '(' after '{functionName}' at position {_position}");
            
        ConsumeChar(); // consume '('
        SkipWhitespace();

        // Try to parse as specific function arguments first
        if (functionName == "box")
        {
            // Try to parse box(x,y,width,height) format
            return TryParseArgs(ParseDoubleArg, ParseDoubleArg, ParseDoubleArg, ParseDoubleArg)
                .Fold<MaskSpecifier>(
                    args => new BoxMask(args.Item1, args.Item2, args.Item3, args.Item4),
                    () => new BoundingBoxMask(ParseExpression(')'))
                );
        }
        else if (functionName == "circle")
        {
            // Try to parse circle(x,y,radius) format
            return TryParseArgs(ParseDoubleArg, ParseDoubleArg, ParseDoubleArg)
                .Fold<MaskSpecifier>(
                    args => new CircleMask(args.Item1, args.Item2, args.Item3),
                    () => new BoundingCircleMask(ParseExpression(')'))
                );
        }
        
        // For other functions that don't have coordinate overloads, only support mask expression
        throw new InvalidOperationException($"Unknown function '{functionName}' at position {_position}");
    }

    private MaskSpecifier ParseClipSegMask(string text)
    {
        // Continue reading until we hit an operator or end
        var sb = new StringBuilder(text);
            
        while (!IsAtEnd())
        {
            SkipWhitespace();
            if (IsAtEnd())
                break;
                
            char ch = PeekChar();
            if (ch == ':' || ch == '|' || ch == '&' || ch == '+' || ch == ')' || ch == '[')
                break;
                
            if (char.IsLetter(ch))
            {
                sb.Append(' ');
                sb.Append(ParseIdentifier());
            }
            else
            {
                break;
            }
        }

        var baseMask = new ClipSegMask(sb.ToString().Trim());
        return ParseThresholdSuffix(baseMask);
    }

    private MaskSpecifier ParseThresholdSuffix(MaskSpecifier baseMask)
    {
        SkipWhitespace();
            
        if (!IsAtEnd() && PeekChar() == ':')
        {
            ConsumeChar(); // consume ':'
            SkipWhitespace();

            if (!TryParseDouble(out double threshold))
                throw new InvalidOperationException($"Expected threshold value after ':' at position {_position}");
            
            if (baseMask is YoloMask yoloMask)
            {
                baseMask = yoloMask with { Threshold = threshold };
            }
            if (baseMask is ClipSegMask clipSegMask)
            {
                baseMask = clipSegMask with { Threshold = threshold };
            }

            SkipWhitespace();
                
            if (!IsAtEnd() && PeekChar() == ':')
            {
                ConsumeChar(); // consume second ':'
                SkipWhitespace();
                    
                if (!TryParseDouble(out double maxVal))
                    throw new InvalidOperationException($"Expected threshold max value after second ':' at position {_position}");

                baseMask = new ThresholdMask(baseMask, maxVal);
            }
        }

        return baseMask;
    }

    private string ParseIdentifier()
    {
        var sb = new StringBuilder();
            
        while (!IsAtEnd() && (char.IsLetterOrDigit(PeekChar()) || PeekChar() == '-' || PeekChar() == '_' || PeekChar() == '.'))
        {
            sb.Append(ConsumeChar());
        }

        return sb.ToString();
    }

    private bool TryParseInteger(out int value)
    {
        var sb = new StringBuilder();
            
        while (!IsAtEnd() && char.IsDigit(PeekChar()))
        {
            sb.Append(ConsumeChar());
        }

        return int.TryParse(sb.ToString(), out value);
    }

    private bool TryParseDouble(out double value)
    {
        var sb = new StringBuilder();
            
        while (!IsAtEnd())
        {
            char ch = PeekChar();
            if (char.IsDigit(ch) || ch == '.')
            {
                sb.Append(ConsumeChar());
            }
            else
            {
                break;
            }
        }

        return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(PeekChar()))
        {
            _position++;
        }
    }

    private char PeekChar()
    {
        return IsAtEnd() ? '\0' : _input[_position];
    }

    private char ConsumeChar()
    {
        return IsAtEnd() ? '\0' : _input[_position++];
    }

    private bool IsAtEnd()
    {
        return _position >= _input.Length;
    }
}