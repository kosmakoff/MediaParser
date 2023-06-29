﻿using Media.ISO;
using Media.ISO.Boxes;
using System;
using System.Buffers;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CommandLine
{
    class Program
    {

        static void DumpBoxHeader(BoxHeader boxHeader)
        {
            var extendedType = boxHeader.ExtendedType != null ? $"Guid={boxHeader.ExtendedType}" : string.Empty;
            Console.WriteLine($"Box={boxHeader.Type} {boxHeader.Type.GetBoxName()} Size={boxHeader.Size} {extendedType}");
        }

        static void DumpBox(Box box, int indent = 0)
        {
            var prefix = new String(' ', indent * 2);
            var extendedType = box.ExtendedType != null ? $"Guid={box.ExtendedType}" : string.Empty;
            Console.WriteLine($"{prefix}Box={box.Name} Size={box.Size} {extendedType} children={box.Children.Count}");
            if (box is TrackFragmentExtendedHeaderBox tfxd)
            {
                Console.WriteLine($"  {prefix}Time={tfxd.Time} Duration={tfxd.Duration}");
            }
            else if (box is TrackFragmentDecodeTimeBox tfdt)
            {
                Console.WriteLine($"  {prefix}Time={tfdt.BaseMediaDecodeTime}");
            }
            else
            {
                foreach (var child in box.Children)
                    DumpBox(child, indent + 1);
            }
        }

        static void Skip(Stream stream, int bytes)
        {
            if (stream.CanSeek)
            {
                stream.Position += bytes;
            }
            else
            {
                var size = Math.Min(bytes, 16 * 1024);
                using var buffer = MemoryPool<byte>.Shared.Rent(size);

                while (bytes != 0)
                {
                    bytes -= stream.Read(buffer.Memory.Span);
                }
            }
        }

        static async Task Main(string[] args)
        {
            var fileOption = new Option<Uri>(
                aliases: new[] { "-i", "--input-file" },
                description: "file to open")
            {
                IsRequired = true
            };
            
            var typeOption = new Option<bool>(
                aliases: new[] { "-t", "--top-level" },
                getDefaultValue: () => false,
                description: "only print top level boxes");

            var rootCommand = new RootCommand("MP4 Box dump");
            rootCommand.AddOption(typeOption);
            rootCommand.AddOption(fileOption);

            // Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.SetHandler(async (inputFile, top) =>
            {
                using var stream = await OpenUri(inputFile);
                if (top)
                {
                    PrintTopLevelBoxes(stream);
                }
                else
                {
                    PrintBoxTree(stream);
                }
            }, fileOption, typeOption);

            // Parse the incoming args and invoke the handler
            var parser = new CommandLineBuilder(rootCommand).Build();
            await parser.InvokeAsync(args);
        }

        private static async Task<Stream> OpenUri(Uri uri)
        {
            if (uri.IsFile)
            {
                return File.OpenRead(uri.LocalPath);
            }
            else
            {
                var client = new HttpClient();
                var respone = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                return await respone.Content.ReadAsStreamAsync();
            }
        }

        private static void PrintBoxTree(Stream stream, int indent = 0)
        {
            if (stream.CanSeek)
            {
                foreach (var box in BoxFactory.Parse(stream))
                {
                    DumpBox(box, indent);
                }
            }
            else
            {
                PrintTopLevelBoxes(stream);
            }
        }

        private static void PrintTopLevelBoxes(Stream stream)
        {
            Span<byte> boxHeader = stackalloc byte[8];
            while (true)
            {
                var bytes = stream.Read(boxHeader);
                if (bytes == 0)
                    break;
                var box = BoxFactory.Parse(boxHeader);
                DumpBox(box);
                Skip(stream, (int)box.Size - 8);
            }
        }
    }
}
