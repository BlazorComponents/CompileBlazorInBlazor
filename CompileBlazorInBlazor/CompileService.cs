﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;


namespace CompileBlazorInBlazor
{
    public class CompileService
    {
        private readonly HttpClient _http;
        private readonly NavigationManager _uriHelper;
        public List<string> CompileLog { get; set; }
        private List<MetadataReference> references { get; set; }


        public CompileService(HttpClient http, NavigationManager uriHelper)
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
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }
                    var name = assembly.GetName().Name + ".dll";
                    Console.WriteLine(name);
                    try
                    {
                        references.Add(MetadataReference.CreateFromStream(await _http.GetStreamAsync(_uriHelper.BaseUri + "_framework/" + name)));
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }


        public Type CompileBlazor(string code)
        {
            CompileLog.Add("Create fileSystem");

            var fileSystem = new EmptyRazorProjectFileSystem();

            CompileLog.Add("Create engine");
            //            Microsoft.AspNetCore.Blazor.Build.
            
            var engine = RazorProjectEngine.Create(RazorConfiguration.Create(RazorLanguageVersion.Version_6_0, "Blazor", new RazorExtension[0]), fileSystem, b =>
            {
                //                RazorExtensions.Register(b);


//                b.SetRootNamespace(DefaultRootNamespace);

                // Turn off checksums, we're testing code generation.
//                b.Features.Add(new SuppressChecksum());

//                if (LineEnding != null)
//                {
//                    b.Phases.Insert(0, new ForceLineEndingPhase(LineEnding));
//                }

                // Including MVC here so that we can find any issues that arise from mixed MVC + Components.
//                Microsoft.AspNetCore.Mvc.Razor.Extensions.RazorExtensions.Register(b);
//
//                // Features that use Roslyn are mandatory for components
//                Microsoft.CodeAnalysis.Razor.CompilerFeatures.Register(b);
//
//                b.Features.Add(new CompilationTagHelperFeature());
//                b.Features.Add(new DefaultMetadataReferenceFeature()
//                {
//                    References = references,
//                });



            });


            CompileLog.Add("Create file");
            var file = new MemoryRazorProjectItem(code, true, "/App", "/App/App.razor");
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
            var assembly = Compile(csCode);

            if (assembly != null)
            {
                CompileLog.Add("Search Blazor component");
                return assembly.GetExportedTypes().FirstOrDefault(i => i.IsSubclassOf(typeof(ComponentBase)));
            }

            return null;
        }


        public Assembly Compile(string code)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview));
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

                stream.Seek(0, SeekOrigin.Begin);

//                var context = new CollectibleAssemblyLoadContext();
                Assembly assemby = AppDomain.CurrentDomain.Load(stream.ToArray());
                return assemby;
            }

            return null;
        }


//        public class CollectibleAssemblyLoadContext : AssemblyLoadContext
//        {
//            public CollectibleAssemblyLoadContext() : base()
//            {
//            }
//
//
//            protected override Assembly Load(AssemblyName assemblyName)
//            {
//                return null;
//            }
//        }


        public string CompileAndRun(string code)
        {
            var assemby = Compile(code);
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