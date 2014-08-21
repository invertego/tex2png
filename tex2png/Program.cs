using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tex2png
{
    class Program
    {
        static void SingleConvert(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var texture = new Texture(stream);

                texture.SaveBitmap(Path.ChangeExtension(path, ".png"));
            }
        }

        static void BatchConvert(string path)
        {
            using (var archive = new ZipArchive(File.OpenRead(path)))
            {
                foreach (var entry in archive.Entries)
                {
                    var extension = Path.GetExtension(entry.Name);

                    if (extension.Equals(".tex", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(entry.FullName);

                        using (var stream = entry.Open())
                        {
                            var texture = new Texture(stream);
                            var directoryName = Path.GetDirectoryName(path);
                            var fullName = Path.ChangeExtension(entry.FullName, ".png");

                            texture.SaveBitmap(Path.Combine(directoryName, fullName));
                        }
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: {0} <filename.tex | filename.ipa>", AppDomain.CurrentDomain.FriendlyName);
                return;
            }

            var path = args[0];
            var extension = Path.GetExtension(path);

            if (extension.Equals(".tex", StringComparison.OrdinalIgnoreCase))
            {
                SingleConvert(path);
            }
            else if (extension.Equals(".ipa", StringComparison.OrdinalIgnoreCase))
            {
                BatchConvert(path);
            }
            else
            {
                Console.WriteLine("Unknown file extension");
            }
        }
    }
}
