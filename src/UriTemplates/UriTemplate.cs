﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tavis
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;

    namespace UriTemplates
    {
        public class UriTemplate
        {


            private const string _UriReservedSymbols = ":/?#[]@!$&'()*+,;=";
            private const string _UriUnreservedSymbols = "-._~";
            private static Dictionary<char, OperatorInfo> _Operators = new Dictionary<char, OperatorInfo>() {
                                        {'\0', new OperatorInfo {Default = true, First = "", Seperator = ',', Named = false, IfEmpty = "",AllowReserved = false}},
                                        {'+', new OperatorInfo {Default = false, First = "", Seperator = ',', Named = false, IfEmpty = "",AllowReserved = true}},
                                        {'.', new OperatorInfo {Default = false, First = ".", Seperator = '.', Named = false, IfEmpty = "",AllowReserved = false}},
                                        {'/', new OperatorInfo {Default = false, First = "/", Seperator = '/', Named = false, IfEmpty = "",AllowReserved = false}},
                                        {';', new OperatorInfo {Default = false, First = ";", Seperator = ';', Named = true, IfEmpty = "",AllowReserved = false}},
                                        {'?', new OperatorInfo {Default = false, First = "?", Seperator = '&', Named = true, IfEmpty = "=",AllowReserved = false}},
                                        {'&', new OperatorInfo {Default = false, First = "&", Seperator = '&', Named = true, IfEmpty = "=",AllowReserved = false}},
                                        {'#', new OperatorInfo {Default = false, First = "#", Seperator = ',', Named = false, IfEmpty = "",AllowReserved = true}}
                                        };

            private readonly string _template;
            private readonly Dictionary<string, object> _Parameters = new Dictionary<string, object>();
            private enum States { CopyingLiterals, ParsingExpression }
            private bool _ErrorDetected = false;
            private StringBuilder _Result;
            private List<string> _ParameterNames;

            private bool _resolvePartially;

            public UriTemplate(string template, bool resolvePartially = false )
            {
                _resolvePartially = resolvePartially;
                _template = template;
            }


            public void SetParameter(string name, object value)
            {
                _Parameters[name] = value;
            }

            public void SetParameter(string name, string value)
            {
                _Parameters[name] = value;
            }

            public void SetParameter(string name, IEnumerable<string> value)
            {
                _Parameters[name] = value;
            }

            public void SetParameter(string name, IDictionary<string, string> value)
            {
                _Parameters[name] = value;
            }

            public IEnumerable<string> GetParameterNames()
            {
                var parameterNames = new List<string>();
                _ParameterNames = parameterNames;
                Resolve();
                _ParameterNames = null;
                return parameterNames;
            }

            public string Resolve()
            {
                var currentState = States.CopyingLiterals;
                _Result = new StringBuilder();
                StringBuilder currentExpression = null;
                foreach (var character in _template.ToCharArray())
                {
                    switch (currentState)
                    {
                        case States.CopyingLiterals:
                            if (character == '{')
                            {
                                currentState = States.ParsingExpression;
                                currentExpression = new StringBuilder();
                            }
                            else if (character == '}')
                            {
                                throw new ArgumentException("Malformed template, unexpected } : " + _Result.ToString());
                            }
                            else
                            {
                                _Result.Append(character);
                            }
                            break;
                        case States.ParsingExpression:
                            if (character == '}')
                            {
                                ProcessExpression(currentExpression);

                                currentState = States.CopyingLiterals;
                            }
                            else
                            {
                                currentExpression.Append(character);
                            }

                            break;
                    }
                }
                if (currentState == States.ParsingExpression)
                {
                    _Result.Append("{");
                    _Result.Append(currentExpression.ToString());

                    throw new ArgumentException("Malformed template, missing } : " + _Result.ToString());
                }

                if (_ErrorDetected)
                {
                    throw new ArgumentException("Malformed template : " + _Result.ToString());
                }
                return _Result.ToString();
            }

            private void ProcessExpression(StringBuilder currentExpression)
            {

                if (currentExpression.Length == 0)
                {
                    _ErrorDetected = true;
                    _Result.Append("{}");
                    return;
                }

                OperatorInfo op = GetOperator(currentExpression[0]);

                var firstChar = op.Default ? 0 : 1;
                bool multivariableExpression = false;

                var varSpec = new VarSpec(op);
                for (int i = firstChar; i < currentExpression.Length; i++)
                {
                    char currentChar = currentExpression[i];
                    switch (currentChar)
                    {
                        case '*':
                            varSpec.Explode = true;
                            break;
                        case ':':  // Parse Prefix Modifier
                            var prefixText = new StringBuilder();
                            currentChar = currentExpression[++i];
                            while (currentChar >= '0' && currentChar <= '9' && i < currentExpression.Length)
                            {
                                prefixText.Append(currentChar);
                                i++;
                                if (i < currentExpression.Length) currentChar = currentExpression[i];
                            }
                            varSpec.PrefixLength = int.Parse(prefixText.ToString());
                            i--;
                            break;
                        case ',':
                            multivariableExpression = true;
                            var success = ProcessVariable(varSpec, multivariableExpression);
                            bool isFirst = varSpec.First;
                            // Reset for new variable
                            varSpec = new VarSpec(op);
                            if (success || !isFirst) varSpec.First = false;

                            break;
                        

                        default:
                            if (IsVarNameChar(currentChar))
                            {
                                varSpec.VarName.Append(currentChar);
                            }
                            else
                            {
                                _ErrorDetected = true;
                            }
                            break;
                    }
                }

                ProcessVariable(varSpec, multivariableExpression);
                if (multivariableExpression && _resolvePartially) _Result.Append("}");
            }

            private bool ProcessVariable(VarSpec varSpec, bool multiVariableExpression = false)
            {
                var varname = varSpec.VarName.ToString();
                if (_ParameterNames != null) _ParameterNames.Add(varname);

                if (!_Parameters.ContainsKey(varname)
                    || _Parameters[varname] == null
                    || (_Parameters[varname] is IList && ((IList) _Parameters[varname]).Count == 0)
                    || (_Parameters[varname] is IDictionary && ((IDictionary) _Parameters[varname]).Count == 0))
                {
                    if (_resolvePartially == true)
                    {
                        if (multiVariableExpression)
                        {
                            if (varSpec.First) _Result.Append("{");
                            _Result.Append(varSpec.ToString());
                        }
                        else
                        {
                            _Result.Append("{");
                            _Result.Append(varSpec.ToString());
                            _Result.Append("}");
                        }
                    }
                    return false;
                }

                if (varSpec.First)
                {
                    _Result.Append(varSpec.OperatorInfo.First);
                }
                else
                {
                    _Result.Append(varSpec.OperatorInfo.Seperator);
                }

                object value = _Parameters[varname];

                // Handle Strings
                if (value is string)
                {
                    var stringValue = (string)value;
                    if (varSpec.OperatorInfo.Named)
                    {
                        AppendName(varname, varSpec.OperatorInfo, string.IsNullOrEmpty(stringValue));
                    }
                    AppendValue(stringValue, varSpec.PrefixLength, varSpec.OperatorInfo.AllowReserved);
                }
                else
                {
                    // Handle Lists
                    var list = value as IList;
                    if (list == null && value is IEnumerable<string>)
                    {
                        list = ((IEnumerable<string>)value).ToList<string>();
                    } ;
                    if (list != null)
                    {
                        if (varSpec.OperatorInfo.Named && !varSpec.Explode)  // exploding will prefix with list name
                        {
                            AppendName(varname, varSpec.OperatorInfo, list.Count == 0);
                        }

                        AppendList(varSpec.OperatorInfo, varSpec.Explode, varname, list);
                    }
                    else
                    {

                        // Handle associative arrays
                        var dictionary = value as IDictionary<string, string>;
                        if (dictionary != null)
                        {
                            if (varSpec.OperatorInfo.Named && !varSpec.Explode)  // exploding will prefix with list name
                            {
                                AppendName(varname, varSpec.OperatorInfo, dictionary.Count() == 0);
                            }
                            AppendDictionary(varSpec.OperatorInfo, varSpec.Explode, dictionary);
                        }
                        else
                        {
                            // If above all fails, convert the object to string using the default object.ToString() implementation
                            var stringValue = value.ToString();
                            if (varSpec.OperatorInfo.Named)
                            {
                                AppendName(varname, varSpec.OperatorInfo, string.IsNullOrEmpty(stringValue));
                            }
                            AppendValue(stringValue, varSpec.PrefixLength, varSpec.OperatorInfo.AllowReserved);
                        }

                    }

                }
                return true;
            }


            private void AppendDictionary(OperatorInfo op, bool explode, IDictionary<string, string> dictionary)
            {
                foreach (string key in dictionary.Keys)
                {
                    _Result.Append(Encode(key, op.AllowReserved));
                    if (explode) _Result.Append('='); else _Result.Append(',');
                    AppendValue(dictionary[key], 0, op.AllowReserved);

                    if (explode)
                    {
                        _Result.Append(op.Seperator);
                    }
                    else
                    {
                        _Result.Append(',');
                    }
                }
                if (dictionary.Count() > 0)
                {
                    _Result.Remove(_Result.Length - 1, 1);
                }
            }

            private void AppendList(OperatorInfo op, bool explode, string variable, IList list)
            {
                foreach (object item in list)
                {
                    if (op.Named && explode)
                    {
                        _Result.Append(variable);
                        _Result.Append("=");
                    }
                    AppendValue(item.ToString(), 0, op.AllowReserved);

                    _Result.Append(explode ? op.Seperator : ',');
                }
                if (list.Count > 0)
                {
                    _Result.Remove(_Result.Length - 1, 1);
                }
            }

            private void AppendValue(string value, int prefixLength, bool allowReserved)
            {

                if (prefixLength != 0)
                {
                    if (prefixLength < value.Length)
                    {
                        value = value.Substring(0, prefixLength);
                    }
                }

                _Result.Append(Encode(value, allowReserved));

            }

            private void AppendName(string variable, OperatorInfo op, bool valueIsEmpty)
            {
                _Result.Append(variable);
                if (valueIsEmpty) { _Result.Append(op.IfEmpty); } else { _Result.Append("="); }
            }



            private bool IsVarNameChar(char c)
            {
                return ((c >= 'A' && c <= 'z') //Alpha
                        || (c >= '0' && c <= '9') // Digit
                        || c == '_'
                        || c == '%'
                        || c == '.');
            }

            private static string Encode(string p, bool allowReserved)
            {

                var result = new StringBuilder();
                foreach (char c in p)
                {
                    if ((c >= 'A' && c <= 'z')   //Alpha
                        || (c >= '0' && c <= '9')  // Digit
                        || _UriUnreservedSymbols.IndexOf(c) != -1  // Unreserved symbols  - These should never be percent encoded
                        || (allowReserved && _UriReservedSymbols.IndexOf(c) != -1))  // Reserved symbols - should be included if requested (+)
                    {
                        result.Append(c);
                    }
                    else
                    {
                       var bytes = Encoding.UTF8.GetBytes(new []{c});
                        foreach (var abyte in bytes)
                        {
                            result.Append(HexEscape(abyte));
                        }

                    }
                }

                return result.ToString();


            }
            public static string HexEscape(byte i)
            {
                var esc = new char[3];
                esc[0] = '%';
                esc[1] = HexDigits[((i & 240) >> 4)];
                esc[2] = HexDigits[(i & 15)];
                return new string(esc);
            }
            public static string HexEscape(char c) {
                var esc = new char[3];
                esc[0] = '%';
                esc[1] = HexDigits[(((int) c & 240) >> 4)];
                esc[2] = HexDigits[((int) c & 15)];
                return new string(esc);
            }
            private static readonly char[] HexDigits = new char[] {'0','1','2','3','4','5','6','7','8','9','A','B','C','D','E','F'};

            private static OperatorInfo GetOperator(char operatorIndicator)
            {
                OperatorInfo op;
                switch (operatorIndicator)
                {

                    case '+':
                    case ';':
                    case '/':
                    case '#':
                    case '&':
                    case '?':
                    case '.':
                        op = _Operators[operatorIndicator];
                        break;

                    default:
                        op = _Operators['\0'];
                        break;
                }
                return op;
            }


            public class OperatorInfo
            {
                public bool Default { get; set; }
                public string First { get; set; }
                public char Seperator { get; set; }
                public bool Named { get; set; }
                public string IfEmpty { get; set; }
                public bool AllowReserved { get; set; }

            }

            public class VarSpec
            {
                private readonly OperatorInfo _operatorInfo;
                public StringBuilder VarName = new StringBuilder();
                public bool Explode = false;
                public int PrefixLength = 0;
                public bool First = true;
                public string FirstChar = "";

                public VarSpec(OperatorInfo operatorInfo)
                {
                    _operatorInfo = operatorInfo;
                }

                public OperatorInfo OperatorInfo
                {
                    get { return _operatorInfo; }
                }

                public override string ToString()
                {
                    return (First ? FirstChar : "") +
                            VarName.ToString()
                           + (Explode ? "*" : "")
                           + (PrefixLength > 0 ? ":" + PrefixLength : "");

                }
            }
        }

        
    }


}
