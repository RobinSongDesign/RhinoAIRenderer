using System;
using System.Collections.Generic;

namespace Chameleon.Cli
{
    /// <summary>Parses "--name value" pairs; an option may be repeated.</summary>
    internal sealed class ArgumentReader
    {
        private readonly Dictionary<string, List<string>> _values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public ArgumentReader(IReadOnlyList<string> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new CliUsageException("Unexpected argument '" + arg + "'.");
                }

                if (i + 1 >= args.Count)
                {
                    throw new CliUsageException("Option '" + arg + "' needs a value.");
                }

                string name = arg.Substring(2);
                if (!_values.TryGetValue(name, out List<string>? list))
                {
                    list = new List<string>();
                    _values[name] = list;
                }

                list.Add(args[++i]);
            }
        }

        public string? Get(string name)
        {
            return _values.TryGetValue(name, out List<string>? list) ? list[list.Count - 1] : null;
        }

        public string Require(string name)
        {
            return Get(name) ?? throw new CliUsageException("Missing required option --" + name + ".");
        }

        public IReadOnlyList<string> GetAll(string name)
        {
            return _values.TryGetValue(name, out List<string>? list) ? list : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }
}
