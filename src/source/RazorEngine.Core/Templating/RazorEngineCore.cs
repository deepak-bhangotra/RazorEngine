﻿using System.Runtime.Remoting.Contexts;

namespace RazorEngine.Templating
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Reflection;

    using Compilation;
    using Compilation.Inspectors;
    using Configuration;
    using Parallel;
    using Text;
    using System.Security;
    using System.Security.Permissions;
    using System.Threading.Tasks;
    using RazorEngine.Compilation.ReferenceResolver;
    internal class RazorEngineCore
    {
        private readonly ITemplateServiceConfiguration _config;
        /// <summary>
        /// We need this for creating the templates.
        /// </summary>
        private readonly RazorEngineService _cached;

        internal RazorEngineCore(ITemplateServiceConfiguration config, RazorEngineService cached)
        {
            Contract.Requires(config != null);
            Contract.Requires(config.TemplateManager != null);

            _config = config;
            _cached = cached;
        }

        /// <summary>
        /// Creates a new <see cref="ExecuteContext"/> for tracking templates.
        /// </summary>
        /// <param name="viewBag">The dynamic view bag.</param>
        /// <returns>The execute context.</returns>
        public virtual ExecuteContext CreateExecuteContext(DynamicViewBag viewBag = null)
        {
            var context = new ExecuteContext(new DynamicViewBag(viewBag));
            return context;
        }

        /// <summary>
        /// Gets the template service configuration.
        /// </summary>
        public ITemplateServiceConfiguration Configuration { get { return _config; } }


        /// <summary>
        /// Compiles the specified template.
        /// </summary>
        /// <param name="key">The string template.</param>
        /// <param name="modelType">The model type.</param>
        public ICompiledTemplate Compile(ITemplateKey key, Type modelType)
        {
            Contract.Requires(key != null);
            var source = Resolve(key);
            var result = CreateTemplateType(source, modelType);
            return new CompiledTemplate(result.Item2, key, source, result.Item1, modelType);
        }
        
        /// <summary>
        /// Creates an instance of <see cref="ITemplate"/> from the specified string template.
        /// </summary>
        /// <param name="template">The compiled template.</param>
        /// <param name="model">The model instance or NULL if no model exists.</param>
        /// <returns>An instance of <see cref="ITemplate"/>.</returns>
        [Pure]
        internal virtual ITemplate CreateTemplate(ICompiledTemplate template, object model)
        {
            var context = CreateInstanceContext(template.TemplateType);
            ITemplate instance = _config.Activator.CreateInstance(context);
            instance.InternalTemplateService = new InternalTemplateService(this, template.Key);
            instance.TemplateService = new TemplateService(_cached);
            instance.RazorEngine = _cached;
            if (model != null)
            {
                instance.SetModel(model);
            }
            return instance;
        }

        /// <summary>
        /// Creates a <see cref="Type"/> that can be used to instantiate an instance of a template.
        /// </summary>
        /// <param name="razorTemplate">The string template.</param>
        /// <param name="modelType">The model type or NULL if no model exists.</param>
        /// <returns>An instance of <see cref="Type"/>.</returns>
        [Pure][SecuritySafeCritical] // This should not be SecuritySafeCritical (make the template classes SecurityCritical instead)
        public virtual Tuple<Type, CompilationData> CreateTemplateType(ITemplateSource razorTemplate, Type modelType)
        {
            var context = new TypeContext
            {
                ModelType = modelType ?? typeof(System.Dynamic.DynamicObject),
                TemplateContent = razorTemplate,
                TemplateType = (_config.BaseTemplateType) ?? typeof(TemplateBase<>)
            };

            foreach (string ns in _config.Namespaces)
                context.Namespaces.Add(ns);

            var service = _config
                .CompilerServiceFactory
                .CreateCompilerService(_config.Language);
            service.Debug = _config.Debug;
#if !RAZOR4
            service.CodeInspectors = _config.CodeInspectors ?? Enumerable.Empty<ICodeInspector>();
#endif
            service.ReferenceResolver = _config.ReferenceResolver ?? new UseCurrentAssembliesReferenceResolver();

            var result = service.CompileType(context);

            return result;
        }


        /// <summary>
        /// Runs the specified template and returns the result.
        /// </summary>
        /// <param name="template">The template to run.</param>
        /// <param name="writer"></param>
        /// <param name="model"></param>
        /// <param name="viewBag">The ViewBag contents or NULL for an initially empty ViewBag.</param>
        /// <returns>The string result of the template.</returns>
#if RAZOR4
        public async Task RunTemplate(ICompiledTemplate template, System.IO.TextWriter writer, object model, DynamicViewBag viewBag)
        
#else
        public void RunTemplate(ICompiledTemplate template, System.IO.TextWriter writer, object model, DynamicViewBag viewBag)
#endif
        {
            if (template == null)
                throw new ArgumentNullException("template");

            var instance = CreateTemplate(template, model);
#if RAZOR4
            await instance.Run(CreateExecuteContext(viewBag), writer);
#else
            instance.Run(CreateExecuteContext(viewBag), writer);
#endif
        }

        /// <summary>
        /// Creates a new <see cref="InstanceContext"/> for creating template instances.
        /// </summary>
        /// <param name="templateType">The template type.</param>
        /// <returns>An instance of <see cref="InstanceContext"/>.</returns>
        [Pure]
        protected internal virtual InstanceContext CreateInstanceContext(Type templateType)
        {
            return new InstanceContext(_config.CachingProvider.TypeLoader, templateType);
        }

        public ITemplateKey GetKey(string cacheName, ResolveType resolveType = ResolveType.Global, ITemplateKey context = null)
        {
            return _config.TemplateManager.GetKey(cacheName, resolveType, context);
        }

        internal virtual ITemplate ResolveInternal(string cacheName, object model, Type modelType, ResolveType resolveType, ITemplateKey context)
        {
            var templateKey = GetKey(cacheName, resolveType, context);
            var compiledTemplate = Compile(templateKey, modelType);
            return CreateTemplate(compiledTemplate, model);
        }

        internal ITemplateSource Resolve(ITemplateKey key)
        {
            return Configuration.TemplateManager.Resolve(key);
        }
    }

}