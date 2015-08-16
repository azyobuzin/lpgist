using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HyperTomlProcessor;
using Octokit;

namespace Lpgist
{
    static class Program
    {
        private static readonly ProductHeaderValue userAgent = new ProductHeaderValue("lpgist");

        public static int Main(string[] args)
        {
            var config = LoadConfig();

            if (args.Length == 0)
            {
                Usage();
                return -1;
            }

            foreach (var s in args)
            {
                switch (s)
                {
                    case "/?":
                    case "-?":
                    case "-help":
                    case "--help":
                        Usage();
                        return -1;
                }
            }

            var isPublic = config.IsPublic;
            var anonymous = string.IsNullOrEmpty(config.GitHubAccessToken);
            string description = null;
            IReadOnlyList<string> lprunArgs = new string[0];

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--private":
                        isPublic = false;
                        continue;
                    case "--public":
                        isPublic = true;
                        continue;
                    case "--anonymous":
                        anonymous = true;
                        continue;
                    case "--description":
                    case "-d":
                        try
                        {
                            description = args[++i];
                        }
                        catch (IndexOutOfRangeException)
                        {
                            Console.WriteLine("Invalid --description argument");
                            return -1;
                        }
                        continue;
                }

                lprunArgs = new ArraySegment<string>(args, i, args.Length - i);
                break;
            }

            string format = null;
            string queryFileName = null;
            foreach (var s in lprunArgs)
            {
                if (s.StartsWith("-format=", StringComparison.OrdinalIgnoreCase))
                    format = s.Substring(8, s.Length - 8);
                else if (!s.StartsWith("-"))
                {
                    queryFileName = s;
                    break;
                }
            }

            var lprunArgsStr = string.Join(" ",
                lprunArgs.Select(x => string.Concat(
                    "\"",
                    x.Replace("\\", "\\\\").Replace("\"", "\\\""),
                    "\"")));

            if (format == null)
            {
                format = config.Format;
                lprunArgsStr = string.Format("-format={0} {1}", format, lprunArgsStr);
            }

            Console.WriteLine("Running lprun...");

            string stdout;
            using (var p = Process.Start(new ProcessStartInfo(config.LprunPath, lprunArgsStr)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true
            }))
            {
                Contract.Assume(p != null);
                stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                {
                    Console.WriteLine(stdout);
                    Console.WriteLine("lprun failed");
                    return p.ExitCode;
                }
            }

            Contract.Assume(queryFileName != null, nameof(queryFileName) + " is a valid file name because lprun succeeded");

            Console.WriteLine("Uploading...");

            var queryFileContent = File.ReadAllText(queryFileName);
            var query = LinqPadQuery.Parse(queryFileContent);

            var newGist = new NewGist()
            {
                Description = description,
                Public = isPublic
            };

            var forBlocksorg = isPublic && format.Equals("html", StringComparison.OrdinalIgnoreCase);

            newGist.Files.Add(Path.GetFileName(queryFileName), queryFileContent);
            newGist.Files.Add("source" + GetSourceExtension(query.Kind), query.Source);
            newGist.Files.Add(
                forBlocksorg ? "index.html" : ("result" + GetResultExtension(format)),
                stdout);

            var client = new GitHubClient(userAgent);
            if (!anonymous)
                client.Credentials = new Credentials(config.GitHubAccessToken);

            var gist = client.Gist.Create(newGist).GetAwaiter().GetResult();

            Console.WriteLine();
            Console.WriteLine(gist.HtmlUrl);

            if (forBlocksorg)
                Console.WriteLine("http://bl.ocks.org/{0}/{1}", gist.Owner?.Login ?? "anonymous", gist.Id);

            return 0;
        }

        private static void Usage()
        {
            Console.WriteLine(@"Lpgist
Uploads your LINQPad query and result to Gist

Usage
  lpgist [options] [lprun args]

Options
  --private
      upload as a private gist
  --public
      upload as a public gist
  --anonymous
      do not use your credentials
  --description <msg>
  -d <msg>
      specify the description of the gist to be uploaded");
        }

        private static Config LoadConfig()
        {
            var fileName = Path.Combine(
                // ReSharper disable once AssignNullToNotNullAttribute
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "config.toml"
            );

            try
            {
                using (var sr = new StreamReader(fileName))
                    return Toml.V04.DeserializeObject<Config>(sr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                var config = Setup();

                using (var sw = new StreamWriter(fileName))
                    Toml.V04.SerializeObject(sw, config);

                return config;
            }
        }

        private static bool YesOrNo(bool defaultValue)
        {
            while (true)
            {
                var s = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(s))
                    return defaultValue;
                if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (s.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                    return false;
                Console.Write("Type yes or no: ");
            }
        }

        private static string InputPassword()
        {
            // IMPROVE ME!!!
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey();
                switch (key.Key)
                {
                    case ConsoleKey.Backspace:
                        if (sb.Length > 0)
                            sb.Length--;
                        Console.Write(' ');
                        break;
                    case ConsoleKey.Enter:
                        return sb.ToString();
                    default:
                        sb.Append(key.KeyChar);
                        Console.Write('\b');
                        break;
                }
            }
        }

        private static Config Setup()
        {
            var config = new Config();
            Console.WriteLine("Lpgist Setup");

            Console.Write("lprun.exe path [lprun.exe]: ");
            var lprun = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(lprun))
                config.LprunPath = lprun.Trim(' ', '"', '\'');

            Console.Write("Default format(text|html|htmlfrag|csv|csvi) [html]: ");
            var format = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(format))
                config.Format = format.Trim();

            Console.Write("Want to log into GitHub? [yes]: ");
            if (YesOrNo(true))
            {
                Console.Write("Username: ");
                var userName = Console.ReadLine()?.Trim();
                Console.Write("Password: ");
                var password = InputPassword().Trim();

                var client = new GitHubClient(userAgent);
                client.Credentials = new Credentials(userName, password);

                const string clientId = "1c6ea6235caf92bd0f0e";
                const string clientSecret = "fd0924a473072503af961a3214a9933e96b930d2";

                var newAuthorization = new NewAuthorization(
                    "Lpgist requires Gist access to upload your queries.",
                    new[] { "gist" });

                ApplicationAuthorization result;
                try
                {
                    result = client.Authorization
                        .Create(clientId, clientSecret, newAuthorization)
                        .GetAwaiter().GetResult();
                }
                catch (TwoFactorRequiredException ex)
                {
                    Console.Write("Two-factor code from {0}: ", ex.TwoFactorType);
                    var code = Console.ReadLine()?.Trim();
                    result = client.Authorization
                        .Create(clientId, clientSecret, newAuthorization, code)
                        .GetAwaiter().GetResult();
                }

                config.GitHubAccessToken = result.Token;

                Console.Write("Want the gists to be public? [yes]: ");
                config.IsPublic = YesOrNo(true);
            }

            Console.WriteLine();

            return config;
        }

        private static string GetSourceExtension(QueryLanguage kind)
        {
            switch (kind)
            {
                case QueryLanguage.Expression:
                case QueryLanguage.Statements:
                case QueryLanguage.Program:
                    return ".cs";
                case QueryLanguage.VBExpression:
                case QueryLanguage.VBStatements:
                case QueryLanguage.VBProgram:
                    return ".vb";
                case QueryLanguage.FSharpExpression:
                case QueryLanguage.FSharpProgram:
                    return ".fsx";
                case QueryLanguage.SQL:
                case QueryLanguage.ESQL:
                    return ".sql";
                default:
                    return ".txt";
            }
        }

        private static string GetResultExtension(string format)
        {
            //Note: LINQPad 5.0.10 can't export CSV
            switch (format.ToLowerInvariant())
            {
                case "html":
                case "htmlfrag":
                    return ".html";
                default:
                    return ".txt";
            }
        }
    }
}
