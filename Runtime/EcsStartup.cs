﻿#if ENABLE_MONO || ENABLE_IL2CPP
#define MORPEH_UNITY
#endif

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Scellecs.Morpeh.Elysium
{
    public sealed class EcsStartup : IDisposable
    {
        public World World { get; private set; }

        private StartupResolver resolver;
        private Dictionary<int, SystemsGroup> systemsGroups;
        private int currentOrder;

        private Queue<ResolveInfo> defferedCommands;
        private Queue<ResolveInfo> directCommands;

        private bool initialized;
        private bool disposed;

        public EcsStartup(IStartupContainer container = null, World world = null)
        {
            World = world.IsNullOrDisposed() ? World.Default : world;
            resolver = new StartupResolver(container);
            systemsGroups = new Dictionary<int, SystemsGroup>();
            currentOrder = 0;
            defferedCommands = new Queue<ResolveInfo>(64);
            directCommands = new Queue<ResolveInfo>(64);
            initialized = false;
            disposed = false;
        }

        public void Initialize(bool updateByUnity)
        {
            if (initialized)
            {
                if (disposed)
                {
                    LogWarning("The EcsStartup has already been disposed. Create a new one to use it.");
                }
                else
                {
                    LogWarning("EcsStartup has already been initialized.");
                }

                return;
            }

            if (World.IsNullOrDisposed())
            {
                World = World.Create();
            }

            World.UpdateByUnity = updateByUnity;

            resolver.BuildFeaturesContainer();
            SetupCommands();
            resolver.BuildSystemsContainer();
            CreateSystemsGroups();
            resolver.Cleanup();
            initialized = true;
        }

        public StartupBuilder AddSystemsGroup() => new StartupBuilder(this, currentOrder++);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float deltaTime)
        {
            if (World.UpdateByUnity == false)
            {
                World.Update(deltaTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FixedUpdate(float fixedDeltaTime)
        {
            if (World.UpdateByUnity == false)
            {
                World.FixedUpdate(fixedDeltaTime);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LateUpdate(float deltaTime)
        {
            if (World.UpdateByUnity == false)
            {
                World.LateUpdate(deltaTime);
                World.CleanupUpdate(deltaTime);
            }
        }

        public void Dispose()
        {
            if (initialized && disposed == false)
            {
                systemsGroups.Clear();
                World.Dispose();
                resolver.Dispose();
                World = null;
                disposed = true;
            }
        }

        private static void LogWarning(string message)
        {
#if MORPEH_UNITY
            UnityEngine.Debug.LogWarning(message);
#else
            Console.WriteLine(message);
#endif
        }
#if VCONTAINER || STARTUP_DI
        private void AddRegistrationInjected(RegistrationDefinition definition, int order, bool deffered, Type type)
        {
            var command = new ResolveInfo()
            {
                definition = definition,
                injected = true,
                type = type,
                order = order
            };

            AddCommand(ref command, deffered);
            resolver.Register(type, definition, true, null);
        }
#endif
        private void AddRegistration(RegistrationDefinition definition, int order, bool deffered, object instance)
        {
            var type = instance.GetType();

            var command = new ResolveInfo()
            {
                definition = definition,
                injected = false,
                type = type,
                order = order
            };

            AddCommand(ref command, deffered);
            resolver.Register(type, definition, false, instance);
        }

        private void AddCommand(ref ResolveInfo command, bool deffered)
        {
            if (deffered)
            {
                defferedCommands.Enqueue(command);
            }
            else
            {
                directCommands.Enqueue(command);
            }
        }

        private SystemsGroup GetOrCreateSystemsGroup(int order)
        {
            if (systemsGroups.TryGetValue(order, out SystemsGroup systemsGroup) == false)
            {
                systemsGroup = systemsGroups[order] = World.CreateSystemsGroup();
            }

            return systemsGroup;
        }

        private void SetupCommands()
        {
            while (defferedCommands.Count > 0)
            {
                var info = defferedCommands.Dequeue();

                if (info.definition == RegistrationDefinition.Feature)
                {
                    var feature = resolver.Resolve(info.type, RegistrationDefinition.Feature, info.injected) as IEcsFeature;
                    feature.Configure(new FeatureBuilder(this, info.order));
                }
                else
                {
                    directCommands.Enqueue(info);
                }
            }
        }

        private void CreateSystemsGroups()
        {
            while (directCommands.Count > 0)
            {
                var info = directCommands.Dequeue();
                var instance = resolver.Resolve(info.type, info.definition, info.injected);
                var systemsGroup = GetOrCreateSystemsGroup(info.order);

                if (info.definition == RegistrationDefinition.Initializer)
                {
                    systemsGroup.AddInitializer(instance as IInitializer);
                }
                else if (info.definition == RegistrationDefinition.System)
                {
                    systemsGroup.AddSystem(instance as ISystem);
                }
            }

            foreach (var group in systemsGroups)
            {
                World.AddSystemsGroup(group.Key, group.Value);
            }
        }

        public readonly struct StartupBuilder
        {
            private readonly EcsStartup ecsStartup;
            private readonly int order;

            public StartupBuilder(EcsStartup ecsStartup, int order)
            {
                this.ecsStartup = ecsStartup;
                this.order = order;
            }
#if VCONTAINER || STARTUP_DI
            public StartupBuilder AddInitializerInjected<T>() where T : class, IInitializer
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.Initializer, order, true, typeof(T));
                return this;
            }

            public StartupBuilder AddUpdateSystemInjected<T>() where T : class, IUpdateSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, true, typeof(T));
                return this;
            }

            public StartupBuilder AddFixedSystemInjected<T>() where T : class, IFixedSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, true, typeof(T));
                return this;
            }

            public StartupBuilder AddLateSystemInjected<T>() where T : class, ILateSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, true, typeof(T));
                return this;
            }

            public StartupBuilder AddCleanupSystemInjected<T>() where T : class, ICleanupSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, true, typeof(T));
                return this;
            }

            public StartupBuilder AddFeatureInjected<T>() where T : class, IEcsFeature
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.Feature, order, true, typeof(T));
                return this;
            }
#endif
            public StartupBuilder AddInitializer<T>(T initializer) where T : class, IInitializer
            {
                ecsStartup.AddRegistration(RegistrationDefinition.Initializer, order, true, initializer);
                return this;
            }

            public StartupBuilder AddUpdateSystem<T>(T system) where T : class, IUpdateSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, true, system);
                return this;
            }

            public StartupBuilder AddFixedSystem<T>(T system) where T : class, IFixedSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, true, system);
                return this;
            }

            public StartupBuilder AddLateSystem<T>(T system) where T : class, ILateSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, true, system);
                return this;
            }

            public StartupBuilder AddCleanupSystem<T>(T system) where T : class, ICleanupSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, true, system);
                return this;
            }

            public StartupBuilder AddFeature<T>(T feature) where T : class, IEcsFeature
            {
                ecsStartup.AddRegistration(RegistrationDefinition.Feature, order, true, feature);
                return this;
            }
        }

        public readonly struct FeatureBuilder
        {
            private readonly EcsStartup ecsStartup;
            private readonly int order;

            public FeatureBuilder(EcsStartup ecsStartup, int order)
            {
                this.ecsStartup = ecsStartup;
                this.order = order;
            }
#if VCONTAINER || STARTUP_DI
            public FeatureBuilder AddInitializerInjected<T>() where T : class, IInitializer
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.Initializer, order, false, typeof(T));
                return this;
            }

            public FeatureBuilder AddUpdateSystemInjected<T>() where T : class, IUpdateSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, false, typeof(T));
                return this;
            }

            public FeatureBuilder AddFixedSystemInjected<T>() where T : class, IFixedSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, false, typeof(T));
                return this;
            }

            public FeatureBuilder AddLateSystemInjected<T>() where T : class, ILateSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, false, typeof(T));
                return this;
            }

            public FeatureBuilder AddCleanupSystemInjected<T>() where T : class, ICleanupSystem
            {
                ecsStartup.AddRegistrationInjected(RegistrationDefinition.System, order, false, typeof(T));
                return this;
            }
#endif
            public FeatureBuilder AddInitializer<T>(T initializer) where T : class, IInitializer
            {
                ecsStartup.AddRegistration(RegistrationDefinition.Initializer, order, false, initializer);
                return this;
            }

            public FeatureBuilder AddUpdateSystem<T>(T system) where T : class, IUpdateSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, false, system);
                return this;
            }

            public FeatureBuilder AddFixedSystem<T>(T system) where T : class, IFixedSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, false, system);
                return this;
            }

            public FeatureBuilder AddLateSystem<T>(T system) where T : class, ILateSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, false, system);
                return this;
            }

            public FeatureBuilder AddCleanupSystem<T>(T system) where T : class, ICleanupSystem
            {
                ecsStartup.AddRegistration(RegistrationDefinition.System, order, false, system);
                return this;
            }
        }
    }
}
