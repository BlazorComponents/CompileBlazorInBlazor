using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Blazor.Components;
using Microsoft.AspNetCore.Blazor.Razor;
using Microsoft.AspNetCore.Blazor.Services;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Razor;

namespace CompileBlazorInBlazor
{
    public class CompileService
    {
        private readonly HttpClient _http;
        private readonly IUriHelper _uriHelper;
        public List<string> CompileLog { get; set; }
        private List<MetadataReference> references { get; set; }


        public CompileService(HttpClient http, IUriHelper uriHelper)
        {
            _http = http;
            _uriHelper = uriHelper;
        }

        public async Task Init()
        {
            if (references == null)
            {
                references = new List<MetadataReference>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    references.Add(
                        MetadataReference.CreateFromStream(
                            await this._http.GetStreamAsync(_uriHelper.GetBaseUri()+ "/_framework/_bin/" + assembly.Location)));
                }
            }
        }


        public async Task<Type> CompileBlazor(string code)
        {
            CompileLog.Add("Create fileSystem");

            var fileSystem = new EmptyRazorProjectFileSystem();

            CompileLog.Add("Create engine");
            var engine = RazorProjectEngine.Create(BlazorExtensionInitializer.DefaultConfiguration, fileSystem, b =>
            {
                BlazorExtensionInitializer.Register(b);
            });


            CompileLog.Add("Create file");
            var file = new MemoryRazorProjectItem(code, true, "/App", "/App/App.cshtml");
            CompileLog.Add("File process and GetCSharpDocument");
            var doc = engine.Process(file).GetCSharpDocument();
            CompileLog.Add("Get GeneratedCode");
            var csCode = doc.GeneratedCode;

            CompileLog.Add("Read Diagnostics");
            foreach (var diagnostic in doc.Diagnostics)
            {
                CompileLog.Add(diagnostic.ToString());
            }

            if (doc.Diagnostics.Any(i => i.Severity == RazorDiagnosticSeverity.Error))
            {
                return null;
            }

            CompileLog.Add(csCode);

            CompileLog.Add("Compile assembly");
            var assembly = await Compile(csCode);

            if (assembly != null)
            {
                CompileLog.Add("Search Blazor component");
                return assembly.GetExportedTypes().FirstOrDefault(i => i.IsSubclassOf(typeof(BlazorComponent)));
            }

            return null;
        }


        public async Task<Assembly> Compile(string code)
        {
            await Init();

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
            foreach (var diagnostic in syntaxTree.GetDiagnostics())
            {
                CompileLog.Add(diagnostic.ToString());
            }

            if (syntaxTree.GetDiagnostics().Any(i => i.Severity == DiagnosticSeverity.Error))
            {
                CompileLog.Add("Parse SyntaxTree Error!");
                return null;
            }

            CompileLog.Add("Parse SyntaxTree Success");

            CSharpCompilation compilation = CSharpCompilation.Create("CompileBlazorInBlazor.Demo", new[] {syntaxTree},
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (MemoryStream stream = new MemoryStream())
            {
                EmitResult result = compilation.Emit(stream);

                foreach (var diagnostic in result.Diagnostics)
                {
                    CompileLog.Add(diagnostic.ToString());
                }

                if (!result.Success)
                {
                    CompileLog.Add("Compilation error");
                    return null;
                }

                CompileLog.Add("Compilation success!");
                Assembly assemby = AppDomain.CurrentDomain.Load(stream.ToArray());
                return assemby;
            }

            return null;
        }


        public async Task<string> CompileAndRun(string code)
        {
            await Init();

            var assemby = await this.Compile(code);
            if (assemby != null)
            {
                var type = assemby.GetExportedTypes().FirstOrDefault();
                var methodInfo = type.GetMethod("Run");
                var instance = Activator.CreateInstance(type);
                return (string) methodInfo.Invoke(instance, new object[] {"my UserName", 12});
            }

            return null;
        }
    }
}