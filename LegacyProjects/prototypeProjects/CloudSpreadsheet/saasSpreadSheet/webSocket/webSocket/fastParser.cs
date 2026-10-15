/*
  Copyright (C) Bharath K A

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
  NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
  DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using muParser;
// Credits to muParser

namespace wsSocket
{

    class fastParser
    {
        private Dictionary<string, Parser> parserList = new Dictionary<string, Parser>();
        private object lockObj = new object();

        public void addNewFormula(string name, string formula)
        {
            lock(lockObj)
            {
                Parser value;
                if (!isFormulaLoaded(name, formula))
                {
                    value = new Parser(Parser.EBaseType.tpDOUBLE);
                    value.SetExpr(formula);
                    parserList.Add(name, value);
                }
            }
        }

        public bool isFormulaLoaded(string name, string formula)
        {
            bool retVal = false;

            lock (lockObj)
            {
                Parser value;
                if (parserList.TryGetValue(name, out value))
                {
                    if (string.Compare(formula, value.GetExpr()) == 0)
                    {
                        retVal = true;
                    }
                    else
                    {
                        parserList.Remove(name);
                    }
                }
            }

            return retVal;
        }

        public double computeFormula(string name, Dictionary<string, double> values)
        {
            double retVal = 0.0;

            lock (lockObj)
            {
                try
                {
                    Parser parser;
                    if (parserList.TryGetValue(name, out parser))
                    {
                        Dictionary<string, double>.Enumerator enumValues = values.GetEnumerator();
                        while (enumValues.MoveNext())
                        {
                            parser.DefineVar(enumValues.Current.Key, new ParserVariable(enumValues.Current.Value));
                        }
                        retVal = parser.Eval();
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error: computeFormula -- Parsing failure ", ex.Message);
                }
            }

            return retVal;
        }

    }
}
