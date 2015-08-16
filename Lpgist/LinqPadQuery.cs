using System;
using System.IO;
using System.Xml;

namespace Lpgist
{
    enum QueryLanguage
    {
        Text,
        Expression,
        Statements,
        Program,
        VBExpression,
        VBStatements,
        VBProgram,
        FSharpExpression,
        FSharpProgram,
        SQL,
        // ReSharper disable once InconsistentNaming
        ESQL
    }

    class LinqPadQuery
    {
        public LinqPadQuery(QueryLanguage kind, string source)
        {
            this.Kind = kind;
            this.Source = source;
        }

        public QueryLanguage Kind { get; }
        public string Source { get; }

        public static LinqPadQuery Parse(string s)
        {
            var kind = QueryLanguage.Expression;
            int endElementLine;
            using (var xr = new XmlTextReader(new StringReader(s)))
            {
                xr.MoveToContent();
                var topElmName = xr.Name; // "Query"
                var kindStr = xr.GetAttribute("Kind");
                if (!string.IsNullOrWhiteSpace(kindStr))
                    Enum.TryParse(kindStr, true, out kind);

                // Skip to </Query>
                if (!xr.IsEmptyElement)
                {
                    do { } while (xr.Read() && (xr.NodeType != XmlNodeType.EndElement || xr.Name != topElmName));
                }

                endElementLine = xr.LineNumber;
            }

            string source;
            using (var sr = new StringReader(s))
            {
                // endElementLine + 1 times
                for (var i = 0; i <= endElementLine; i++)
                    sr.ReadLine();

                source = sr.ReadToEnd();
            }

            return new LinqPadQuery(kind, source);
        }
    }
}
