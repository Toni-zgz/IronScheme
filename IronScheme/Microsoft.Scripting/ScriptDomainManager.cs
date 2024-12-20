/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Runtime.Serialization;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Generation;
using Microsoft.Scripting.Utils;

namespace Microsoft.Scripting
{
    [Serializable]
    class InvalidImplementationException : Exception {
        public InvalidImplementationException()
            : base() {
        }

        public InvalidImplementationException(string message)
            : base(message) {
        }

        public InvalidImplementationException(string message, Exception e)
            : base(message, e) {
        }
    }

    [Serializable]
    class MissingTypeException : Exception {
        public MissingTypeException() {
        }

        public MissingTypeException(string name, Exception e) : 
            base(String.Format(Resources.MissingType, name), e) {
        }
    }

    public sealed class ScriptDomainManager {

        #region Fields and Initialization

        private static readonly object _singletonLock = new object();
        private static ScriptDomainManager _singleton;
        private readonly IScriptHost _host;
        private readonly Snippets _snippets;
        private readonly ScriptEnvironment _environment;
        
        public Snippets Snippets { get { return _snippets; } }
        public ScriptEnvironment Environment { get { return _environment; } }

        /// <summary>
        /// Gets the <see cref="ScriptDomainManager"/> associated with the current AppDomain. 
        /// If there is none, creates and initializes a new environment using setup information associated with the AppDomain 
        /// or stored in a configuration file.
        /// </summary>
        public static ScriptDomainManager CurrentManager {
            get {
                ScriptDomainManager result;
                TryCreateLocal(out result);
                return result;
            }
        }

        public IScriptHost Host {
            get { return _host; }
        }

        /// <summary>
        /// Creates a new local <see cref="ScriptDomainManager"/> unless it already exists. 
        /// Returns either <c>true</c> and the newly created environment initialized according to the provided setup information
        /// or <c>false</c> and the existing one ignoring the specified setup information.
        /// </summary>
        internal static bool TryCreateLocal(out ScriptDomainManager manager) {

            bool new_created = false;

            if (_singleton == null) {

                lock (_singletonLock) {
                    if (_singleton == null) {
                        ScriptDomainManager singleton = new ScriptDomainManager();
                        _singleton = singleton;
                        new_created = true;
                    }
                }

            }

            manager = _singleton;
            return new_created;
        }

        /// <summary>
        /// Initializes environment according to the setup information.
        /// </summary>
        private ScriptDomainManager() {
            // create local environment for the host:
            _environment = new ScriptEnvironment(this);
            _host = new ScriptHost(_environment);

            // initialize snippets:
            _snippets = new Snippets();
        }

        #endregion
       
        #region Language Providers

        /// <summary>
        /// Singleton for each language.
        /// </summary>
        private sealed class LanguageProviderDesc {

            private string _assemblyName;
            private string _typeName;
            private LanguageProvider _provider;
            private Type _type;

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")] // TODO: fix
            public string AssemblyName {
                get { return _assemblyName; }
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")] // TODO: fix
            public string TypeName {
                get { return _typeName; }
            }

            public LanguageProvider Provider {
                get { return _provider; }
            }

            public LanguageProviderDesc(Type type) {
                Debug.Assert(type != null);

                _type = type;
                _assemblyName = null;
                _typeName = null;
                _provider = null;
            }

            public LanguageProviderDesc(string typeName, string assemblyName) {
                Debug.Assert(typeName != null && assemblyName != null);

                _assemblyName = assemblyName;
                _typeName = typeName;
                _provider = null;
            }

            /// <summary>
            /// Must not be called under a lock as it can potentially call a user code.
            /// </summary>
            /// <exception cref="MissingTypeException"><paramref name="languageId"/></exception>
            /// <exception cref="InvalidImplementationException">The language provider's implementation failed to instantiate.</exception>
            public LanguageProvider LoadProvider(ScriptDomainManager manager) {
                if (_provider == null) {
                    
                    if (_type == null) {
                        try {
                            _type = Assembly.Load(_assemblyName).GetType(_typeName, true);
                        } catch (Exception e) {
                            throw new MissingTypeException(MakeAssemblyQualifiedName(_assemblyName, _typeName), e);
                        }
                    }

                    lock (manager._languageProvidersLock) {
                        manager._languageTypes[_type.AssemblyQualifiedName] = this;
                    }

                    // needn't to be locked, we can create multiple LPs:
                    LanguageProvider provider = ReflectionUtils.CreateInstance<LanguageProvider>(_type, manager);
                    _provider = provider;
                }
                return _provider;
            }
        }

        private readonly object _languageProvidersLock = new object();
        private readonly Dictionary<string, LanguageProviderDesc> _languageIds = new Dictionary<string, LanguageProviderDesc>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LanguageProviderDesc> _languageTypes = new Dictionary<string, LanguageProviderDesc>();

        public void RegisterLanguageProvider(string assemblyName, string typeName, params string[] identifiers) {
            RegisterLanguageProvider(assemblyName, typeName, false, identifiers);
        }

        public void RegisterLanguageProvider(string assemblyName, string typeName, bool overrideExistingIds, params string[] identifiers) {
            Contract.RequiresNotNull(identifiers, "identifiers");

            LanguageProviderDesc singleton_desc;
            bool add_singleton_desc = false;
            string aq_name = MakeAssemblyQualifiedName(typeName, assemblyName);

            lock (_languageProvidersLock) {
                if (!_languageTypes.TryGetValue(aq_name, out singleton_desc)) {
                    add_singleton_desc = true;
                    singleton_desc = new LanguageProviderDesc(typeName, assemblyName);
                }

                // check for conflicts:
                if (!overrideExistingIds) {
                    for (int i = 0; i < identifiers.Length; i++) {
                        LanguageProviderDesc desc;
                        if (_languageIds.TryGetValue(identifiers[i], out desc) && !ReferenceEquals(desc, singleton_desc)) {
                            throw new InvalidOperationException("Conflicting Ids");
                        }
                    }
                }

                // add singleton LP-desc:
                if (add_singleton_desc)
                    _languageTypes.Add(aq_name, singleton_desc);

                // add id mapping to the singleton LP-desc:
                for (int i = 0; i < identifiers.Length; i++) {
                    _languageIds[identifiers[i]] = singleton_desc;
                }
            }
        }

        /// <summary>
        /// Throws an exception on failure.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="type"/></exception>
        /// <exception cref="ArgumentException"><paramref name="type"/></exception>
        /// <exception cref="MissingTypeException"><paramref name="languageId"/></exception>
        /// <exception cref="InvalidImplementationException">The language provider's implementation failed to instantiate.</exception>
        public LanguageProvider GetLanguageProvider(Type type) {
            Contract.RequiresNotNull(type, "type");
            if (!type.IsSubclassOf(typeof(LanguageProvider))) throw new ArgumentException("Invalid type - should be subclass of LanguageProvider"); // TODO

            LanguageProviderDesc desc = null;
            
            lock (_languageProvidersLock) {
                if (!_languageTypes.TryGetValue(type.AssemblyQualifiedName, out desc)) {
                    desc = new LanguageProviderDesc(type);
                    _languageTypes[type.AssemblyQualifiedName] = desc;
                }
            }

            if (desc != null) {
                return desc.LoadProvider(this);
            }

            // not found, not registered:
            throw new ArgumentException(Resources.UnknownLanguageProviderType);
        }

        internal string[] GetLanguageIdentifiers(Type type, bool extensionsOnly) {
            if (type != null && !type.IsSubclassOf(typeof(LanguageProvider))) {
                throw new ArgumentException("Invalid type - should be subclass of LanguageProvider"); // TODO
            }

            bool get_all = type == null;
            List<string> result = new List<string>();

            lock (_languageTypes) {
                LanguageProviderDesc singleton_desc = null;
                if (!get_all && !_languageTypes.TryGetValue(type.AssemblyQualifiedName, out singleton_desc)) {
                    return ArrayUtils.EmptyStrings;
                }

                foreach (KeyValuePair<string, LanguageProviderDesc> entry in _languageIds) {
                    if (get_all || ReferenceEquals(entry.Value, singleton_desc)) {
                        if (!extensionsOnly || IsExtensionId(entry.Key)) {
                            result.Add(entry.Key);
                        }
                    }
                }
            }

            return result.ToArray();
        }

        /// <exception cref="ArgumentNullException"><paramref name="languageId"/></exception>
        /// <exception cref="MissingTypeException"><paramref name="languageId"/></exception>
        /// <exception cref="InvalidImplementationException">The language provider's implementation failed to instantiate.</exception>
        public bool TryGetLanguageProvider(string languageId, out LanguageProvider provider) {
            Contract.RequiresNotNull(languageId, "languageId");

            bool result;
            LanguageProviderDesc desc;

            lock (_languageProvidersLock) {
                result = _languageIds.TryGetValue(languageId, out desc);
            }

            provider = result ? desc.LoadProvider(this) : null;

            return result;
        }

        /// <exception cref="ArgumentNullException"><paramref name="languageId"/></exception>
        /// <exception cref="ArgumentException">no language registered under languageId</exception>
        /// <exception cref="MissingTypeException"><paramref name="languageId"/></exception>
        /// <exception cref="InvalidImplementationException">The language provider's implementation failed to instantiate.</exception>
        public LanguageProvider GetLanguageProvider(string languageId) {
            Contract.RequiresNotNull(languageId, "languageId");

            LanguageProvider result;

            if (!TryGetLanguageProvider(languageId, out result)) {
                throw new ArgumentException(Resources.UnknownLanguageId);
            }

            return result;
        }


        public bool TryGetLanguageProviderByFileExtension(string extension, out LanguageProvider provider) {
            if (String.IsNullOrEmpty(extension)) {
                provider = null;
                return false;
            }

            // TODO: separate hashtable for extensions (see CodeDOM config)
            if (extension[0] != '.') extension = '.' + extension;
            return TryGetLanguageProvider(extension, out provider);
        }

        public string[] GetRegisteredFileExtensions() {
            return GetLanguageIdentifiers(null, true);
        }

        // TODO: separate hashtable for extensions (see CodeDOM config)
        private bool IsExtensionId(string id) {
            return id.StartsWith(".");
        }

        private static string MakeAssemblyQualifiedName(string typeName, string assemblyName) {
            return String.Concat(typeName, ", ", assemblyName);
        }

        #endregion


        #region Modules

        public ScriptModule CompileModule(string name, SourceUnit sourceUnit) {
            return CompileModule(name, null, null, null, sourceUnit);
        }

        /// <summary>
        /// Compiles a list of source units into a single module.
        /// <c>scope</c> can be <c>null</c>.
        /// <c>options</c> can be <c>null</c>.
        /// <c>errorSink</c> can be <c>null</c>.
        /// </summary>
        public ScriptModule CompileModule(string name, Scope scope, ErrorSink errorSink, 
            params SourceUnit[] sourceUnits) {

            Contract.RequiresNotNull(name, "name");
            Contract.RequiresNotNullItems(sourceUnits, "sourceUnits");

            // TODO: Two phases: parse/compile?
            
            // compiles all source units:
            ScriptCode[] scriptCodes = new ScriptCode[sourceUnits.Length];
            for (int i = 0; i < sourceUnits.Length; i++) {
                scriptCodes[i] = LanguageContext.FromEngine(sourceUnits[i].Engine).CompileSourceCode(sourceUnits[i], errorSink);
            }

            return CreateModule(name, scope, scriptCodes);
        }

        /// <summary>
        /// Module creation factory. The only way how to create a module.
        /// </summary>
        public ScriptModule CreateModule(string name, params ScriptCode[] scriptCodes) {
            return CreateModule(name, null, scriptCodes);
        }


        /// <summary>
        /// Module creation factory. The only way how to create a module.
        /// Modules compiled from a single source file unit get <see cref="ScriptModule.FileName"/> property set to a host 
        /// normalized full path of that source unit. The property is set to a <c>null</c> reference for other modules.
        /// <c>scope</c> can be <c>null</c>.
        /// 
        /// Ensures creation of module contexts for all languages whose code is assembled into the module.
        /// </summary>
        public ScriptModule CreateModule(string name, Scope scope, params ScriptCode[] scriptCodes) {
            Contract.RequiresNotNull(name, "name");
            Contract.RequiresNotNullItems(scriptCodes, "scriptCodes");

            CodeGen.SymbolWriters.Clear();

            OptimizedModuleGenerator generator = null;

            if (scope == null)
            {
                if (scriptCodes.Length > 0)
                {
                    generator = OptimizedModuleGenerator.Create(name, scriptCodes);
                    scope = generator.GenerateScope();
                }
                else
                {
                    scope = new Scope();
                }
            }
            
            ScriptModule result = new ScriptModule(name, scope, scriptCodes);

            CodeGen.SymbolWriters.Clear();

            {
              if (name == "ironscheme.boot.new")
              {
                return result;
              }
            }

            // single source file unit modules have unique full path:
            if (scriptCodes.Length == 1) {
                result.FileName = scriptCodes[0].SourceUnit.Id;
            } else {
                result.FileName = null;
            }

            // Initializes module contexts for all contained optimized script codes;
            // Module contexts stored in optimized module's code contexts cannot be changed from now on.
            // Such change would require the CodeContexts to be overwritten.
            if (generator != null) {
                generator.BindGeneratedCodeToModule(result);
            } else {
                foreach (ScriptCode code in scriptCodes) {
                    code.LanguageContext.EnsureModuleContext(result);
                }
            }
            
            _host.ModuleCreated(result);
            return result;
        }

        #endregion

        #region TODO


        // TODO: remove or reduce     
        private static ScriptDomainOptions _options = new ScriptDomainOptions();

        // TODO: remove or reduce
        public static ScriptDomainOptions Options {
            get { return _options; }
        }

        #endregion
    }
}
