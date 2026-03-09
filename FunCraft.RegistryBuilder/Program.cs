namespace FunCraft.RegistryBuilder
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: .\\JsonSchemaGenerator.exe <generated-folder> <output-path>");
                Console.WriteLine();
                Console.WriteLine("Before using, please read README.MD, section Getting Started.");
                Console.WriteLine("Output path is always a file, not a folder!");
                Console.WriteLine("Example:");
                Console.WriteLine("\t.\\FunCraft.JsonSchemaGenerator C:\\server_1.21.10\\generated ..\\FunCraft.Protocol\\Resources\\registries.bin");
                return 1;
            }

            string generatedFolder = args[0];
            string outputPath = args[1];

            if (!Directory.Exists(generatedFolder))
            {
                Console.WriteLine($"ERROR: generated folder not found: {generatedFolder}");
                return 1;
            }

            if (!File.Exists(Path.Combine(generatedFolder, "reports", "registries.json")))
            {
                Console.WriteLine($"ERROR: reports/registries.json not found in {generatedFolder}");
                Console.WriteLine("Run the Minecraft data generator first with --all");
                return 1;
            }

            Console.WriteLine($"Reading: {generatedFolder}");
            Console.WriteLine($"Output:  {outputPath}");
            Console.WriteLine();

            try
            {
                var bundler = new RegistryBundler(generatedFolder);
                bundler.Write(outputPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex}");
                return 1;
            }
        }
    }
}
