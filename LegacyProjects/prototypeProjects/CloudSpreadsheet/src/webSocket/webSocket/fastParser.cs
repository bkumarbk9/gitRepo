// Copyright (C) 2024 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
