//-----------------------------------------------------------------------
// <copyright file="ARCoreAnalytics.cs" company="Google LLC">
//
// Copyright 2019 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------

namespace Google.XR.ARCoreExtensions.Editor.Internal
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using UnityEditor;

    // NOTE: This class's Clearcut/Protobuf-based telemetry reporting (SendAnalytics,
    // OnAnalyticsUpdate, and their supporting ArcoreClearcut/ArcoreSdkLog/LogRequestUtils
    // files and the bundled Google.Protobuf.dll) has been removed. That unversioned,
    // non-strong-named Google.Protobuf.dll collided with Unity 6's own internal
    // Google.Protobuf assembly (used by UnityEditor's MsBuildCompilation pipe), causing a
    // TypeLoadException on every editor domain reload. The EnableAnalytics toggle in
    // Project Settings is kept as a no-op so ARCoreAnalyticsGUI.cs still compiles.
    [InitializeOnLoad]
    [Serializable]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:ElementsMustBeDocumented",
        Justification = "Internal")]
    public class ARCoreAnalytics
    {
        public bool EnableAnalytics;
        private const string _enableAnalyticsKey = "EnableGoogleARCoreExtensionsAnalytics";

        static ARCoreAnalytics()
        {
            Instance = new ARCoreAnalytics();
            Instance.Load();
        }

        public static ARCoreAnalytics Instance { get; private set; }

        /// <summary>
        /// Loads analytics settings.
        /// </summary>
        public void Load()
        {
            EnableAnalytics = EditorPrefs.GetBool(_enableAnalyticsKey, true);
        }

        /// <summary>
        /// Saves current analytics preferences.
        /// </summary>
        public void Save()
        {
            EditorPrefs.SetBool(_enableAnalyticsKey, EnableAnalytics);
        }
    }
}
