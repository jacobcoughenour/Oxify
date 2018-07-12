using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;

namespace Oxify {

	internal class Program {

		private readonly static Regex isUsingReg = new Regex("^using\\s+?[^\\(]+\\;$");
		private readonly static string oxinsert = "//OX.INSERT(";
		private readonly static string oxdebug = "//OX.DEBUGENABLE";
		private readonly static string oxdebugstart = "//OX.DEBUGSTART";
		private readonly static string oxdebugend = "//OX.DEBUGEND";
		
		public Program() {}

		private static string GetNameSpace(string line) {
			return line.Trim(' ', '{').Remove(0, "namespace ".Length);
		}

		private static bool IsUsingLine(string line) {
			return isUsingReg.IsMatch(line);
		}
		
		private static void Put(string message) {
			Console.WriteLine($"[Oxify] {message}");
		}

		private static int Main(string[] args) {

			if (args.Length != 4) {
				Console.WriteLine("Usage: Oxify.exe \"Source\" \"Target\" \"PluginName\" \"VersionNumber\".");
				return 1;
			}

			Put($"Merging Plugin - {args[2]} v{args[3]}");

			string pluginauthor = "UNKNOWN";
			string pluginresourceid = "UNKNOWN";
			string pluginurl = "UNKNOWN";
			string githuburl = "UNKNOWN";

			// write file to target (args[1])
			using (StreamWriter streamWriter = File.CreateText(args[1])) {

				// get all .cs files in directory that aren't AssumblyInfo.cs
				var files = Directory.EnumerateFiles(args[0], "*.cs", SearchOption.AllDirectories)
					.Where(s => !s.EndsWith("AssemblyInfo.cs"));

				Put($"Found {files.Count()} .cs files in {args[0]}");


				// First pass
				// look for flags and metadata

				bool isDebugEnabled = false;

				// look for OX.DEBUG flag
				foreach (string curfile in files) {
					// for each line in file
					foreach (string line in new List<string>(File.ReadAllLines(curfile))) {
						// found debug flag
						if (line.Contains(oxdebug)) {
							Put("Debug Enabled");
							isDebugEnabled = true;
							break;
						}
					}
				}

				// Second pass
				// find all the "using"s
				// store all the lines by their namespaces

				HashSet<string> usings = new HashSet<string>();
				Dictionary<string, List<string>> namespaces = new Dictionary<string, List<string>>();

				// for each file in directory
				foreach (string curfile in files) {

					Put($"Parsing {curfile.Substring(args[0].Length + 1)}");
					
					// current namespace for file
					string curnamespace = string.Empty;

					int bracketdepth = 0;
					bool insideDebugBlock = false;

					// for each line in file
					foreach (string line in new List<string>(File.ReadAllLines(curfile))) {

						int newdepth = bracketdepth + (line.Count(f => f == '{') - line.Count(f => f == '}'));
						
						// enter debug block
						if (line.Contains(oxdebugstart))
							insideDebugBlock = true;

						if (isDebugEnabled || !insideDebugBlock) {
							if (bracketdepth == 0) {

								// if namespace
								if (line.TrimStart(' ').StartsWith("namespace ")) {

									// extract the name of the namespace
									curnamespace = GetNameSpace(line);

									// if this namespace hasn't already been registered
									if (!namespaces.ContainsKey(curnamespace)) {
										// register it
										namespaces.Add(curnamespace, new List<string>());
										Put($"Added Namespace {curnamespace}");
									}

								} else if (IsUsingLine(line)) // if line is a 'using ...;' line
									// add to usings
									usings.Add(line);

							} else if (newdepth != 0) {

								// line contains a OX.INSERT call
								if (line.Contains(oxinsert)) {
									
									// extract the args
									string[] insertargs = Regex.Match(line, @"\(([^)]*)\)").Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

									// match the indentation
									string indent = line.Substring(0, line.IndexOf('/'));

									// TODO
									// add ability to insert enviroment variables
									// and build variables like the version

									if (insertargs[0] == "PluginInfo") {
										pluginauthor = insertargs[1];
										pluginurl = insertargs[2];
										pluginresourceid = Regex.Match(pluginurl, "([0-9]{4})").Groups[0].Value; // extract resource id from oxide url
										githuburl = insertargs[3];
										namespaces[curnamespace].Add($"{indent}[Info(\"{args[2]}\", \"{pluginauthor}\", \"{args[3]}\", ResourceId = {pluginresourceid})]");

										Put($"Inserted {insertargs[0]}");
									}

								} else {
									// add line to namespace
									namespaces[curnamespace].Add(line);
								}
							}
						}

						// exit debug block
						if (line.Contains(oxdebugend))
							insideDebugBlock = false;
						
						// store new bracket depth
						bracketdepth = newdepth;
					}
					
				}


				// build the output file

				StringBuilder output = new StringBuilder();

				// create header with build info

				List<string> header = new List<string>() {
					$"{args[2]}.cs generated by Oxify v{Assembly.GetExecutingAssembly().GetName().Version.ToString()} - {DateTime.Now.ToString("G")}",
					$"PluginInfo: Title = \"{args[2]}\", Author = \"{pluginauthor}\", Version = \"{args[3]}\", ResourceId = {pluginresourceid}",
					$"OxideMod: {pluginurl}",
					$"GitHub: {githuburl}"
				};

				if (isDebugEnabled)
					header.Add($"Flags: OX.DEBUGENABLE");

				// create border around header
				string longest = header.OrderByDescending(s => s.Length).First();
				string border = $"{new String('/', longest.Length + 5)}";

				// add header
				output.AppendLine(border);
				foreach (string hs in header) {
					output.AppendLine($"// {hs}");
				}
				output.AppendLine(border);
				output.AppendLine("\n");

				// add using lines
				output.AppendLine(string.Join("\n", usings));

				// add code lines by namespace
				foreach (var ns in namespaces) {
					output.AppendLine($"\nnamespace {ns.Key} {"{"}");
					output.AppendLine(string.Join("\n", ns.Value));
					output.AppendLine("}");
				}

				// write to file
				streamWriter.Write(output.ToString()); 
			}

			Put($"Saved to {args[1]}");
			Put("Done!");

			return 0;
		}
	}
}