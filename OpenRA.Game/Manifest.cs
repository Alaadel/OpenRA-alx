#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA
{
	public interface IGlobalModData { }

	public sealed class TerrainFormat : IGlobalModData
	{
		public readonly string Type;
		public readonly IReadOnlyDictionary<string, MiniYaml> Metadata;
		public TerrainFormat(MiniYaml yaml)
		{
			Type = yaml.Value;
			Metadata = new ReadOnlyDictionary<string, MiniYaml>(yaml.ToDictionary());
		}
	}

	public sealed class SpriteSequenceFormat : IGlobalModData
	{
		public readonly string Type;
		public readonly IReadOnlyDictionary<string, MiniYaml> Metadata;
		public SpriteSequenceFormat(MiniYaml yaml)
		{
			Type = yaml.Value;
			Metadata = new ReadOnlyDictionary<string, MiniYaml>(yaml.ToDictionary());
		}
	}

	public class ModMetadata
	{
		// FieldLoader used here, must matching naming in YAML.
#pragma warning disable IDE1006 // Naming Styles
		[FluentReference]
		readonly string Title;
		public readonly string Version;
		public readonly string Website;
		public readonly string WebIcon32;
		[FluentReference]
		readonly string WindowTitle;
		public readonly bool Hidden;
#pragma warning restore IDE1006 // Naming Styles

		public string TitleTranslated => FluentProvider.GetString(Title);
		public string WindowTitleTranslated => WindowTitle != null ? FluentProvider.GetString(WindowTitle) : null;
	}

	/// <summary>Describes what is to be loaded in order to run a mod.</summary>
	public sealed class Manifest : IDisposable
	{
		public readonly string Id;
		public readonly IReadOnlyPackage Package;
		public readonly ModMetadata Metadata;
		public readonly string[]
			Rules, ServerTraits,
			Sequences, ModelSequences, Cursors, Chrome, ChromeLayout,
			Weapons, Voices, Notifications, Music, Translations, TileSets,
			ChromeMetrics, MapCompatibility, Missions, Hotkeys;

		public readonly IReadOnlyDictionary<string, string> MapFolders;
		public readonly MiniYaml FileSystem;
		public readonly MiniYaml LoadScreen;
		public readonly string DefaultOrderGenerator;

		public readonly string[] Assemblies = Array.Empty<string>();
		public readonly string[] SoundFormats = Array.Empty<string>();
		public readonly string[] SpriteFormats = Array.Empty<string>();
		public readonly string[] PackageFormats = Array.Empty<string>();
		public readonly string[] VideoFormats = Array.Empty<string>();
		public readonly bool AllowUnusedTranslationsInExternalPackages = true;
		public readonly int FontSheetSize = 512;
		public readonly int CursorSheetSize = 512;

		readonly string[] reservedModuleNames =
		{
			"Include", "Metadata", "FileSystem", "MapFolders", "Rules",
			"Sequences", "ModelSequences", "Cursors", "Chrome", "Assemblies", "ChromeLayout", "Weapons",
			"Voices", "Notifications", "Music", "Translations", "TileSets", "ChromeMetrics", "Missions", "Hotkeys",
			"ServerTraits", "LoadScreen", "DefaultOrderGenerator", "SupportsMapsFrom", "SoundFormats", "SpriteFormats", "VideoFormats",
			"RequiresMods", "PackageFormats", "AllowUnusedTranslationsInExternalPackages", "FontSheetSize", "CursorSheetSize"
		};

		readonly TypeDictionary modules = new();
		readonly Dictionary<string, MiniYaml> yaml;

		bool customDataLoaded;

		public Manifest(string modId, IReadOnlyPackage package)
		{
			Id = modId;
			Package = package;

			var stringPool = new HashSet<string>(); // Reuse common strings in YAML
			var nodes = MiniYaml.FromStream(package.GetStream("mod.yaml"), $"{package.Name}:mod.yaml", stringPool: stringPool);
			for (var i = nodes.Count - 1; i >= 0; i--)
			{
				if (nodes[i].Key != "Include")
					continue;

				// Replace `Includes: filename.yaml` with the contents of filename.yaml
				var filename = nodes[i].Value.Value;
				var contents = package.GetStream(filename);
				if (contents == null)
					throw new YamlException($"{nodes[i].Location}: File `{filename}` not found.");

				nodes.RemoveAt(i);
				nodes.InsertRange(i, MiniYaml.FromStream(contents, $"{package.Name}:{filename}", stringPool: stringPool));
			}

			// Merge inherited overrides
			yaml = new MiniYaml(null, MiniYaml.Merge(new[] { nodes })).ToDictionary();

			Metadata = FieldLoader.Load<ModMetadata>(yaml["Metadata"]);

			// TODO: Use fieldloader
			MapFolders = YamlDictionary(yaml, "MapFolders");

			if (!yaml.TryGetValue("FileSystem", out FileSystem))
				throw new InvalidDataException("`FileSystem` section is not defined.");

			Rules = YamlList(yaml, "Rules");
			Sequences = YamlList(yaml, "Sequences");
			ModelSequences = YamlList(yaml, "ModelSequences");
			Cursors = YamlList(yaml, "Cursors");
			Chrome = YamlList(yaml, "Chrome");
			ChromeLayout = YamlList(yaml, "ChromeLayout");
			Weapons = YamlList(yaml, "Weapons");
			Voices = YamlList(yaml, "Voices");
			Notifications = YamlList(yaml, "Notifications");
			Music = YamlList(yaml, "Music");
			Translations = YamlList(yaml, "Translations");
			TileSets = YamlList(yaml, "TileSets");
			ChromeMetrics = YamlList(yaml, "ChromeMetrics");
			Missions = YamlList(yaml, "Missions");
			Hotkeys = YamlList(yaml, "Hotkeys");

			ServerTraits = YamlList(yaml, "ServerTraits");

			if (!yaml.TryGetValue("LoadScreen", out LoadScreen))
				throw new InvalidDataException("`LoadScreen` section is not defined.");

			// Allow inherited mods to import parent maps.
			var compat = new List<string> { Id };

			if (yaml.TryGetValue("SupportsMapsFrom", out var entry))
				compat.AddRange(entry.Value.Split(',').Select(c => c.Trim()));

			MapCompatibility = compat.ToArray();

			if (yaml.TryGetValue("DefaultOrderGenerator", out entry))
				DefaultOrderGenerator = entry.Value;

			if (yaml.TryGetValue("Assemblies", out entry))
				Assemblies = FieldLoader.GetValue<string[]>("Assemblies", entry.Value);

			if (yaml.TryGetValue("PackageFormats", out entry))
				PackageFormats = FieldLoader.GetValue<string[]>("PackageFormats", entry.Value);

			if (yaml.TryGetValue("SoundFormats", out entry))
				SoundFormats = FieldLoader.GetValue<string[]>("SoundFormats", entry.Value);

			if (yaml.TryGetValue("SpriteFormats", out entry))
				SpriteFormats = FieldLoader.GetValue<string[]>("SpriteFormats", entry.Value);

			if (yaml.TryGetValue("VideoFormats", out entry))
				VideoFormats = FieldLoader.GetValue<string[]>("VideoFormats", entry.Value);

			if (yaml.TryGetValue("AllowUnusedTranslationsInExternalPackages", out entry))
				AllowUnusedTranslationsInExternalPackages =
					FieldLoader.GetValue<bool>("AllowUnusedTranslationsInExternalPackages", entry.Value);

			if (yaml.TryGetValue("FontSheetSize", out entry))
				FontSheetSize = FieldLoader.GetValue<int>("FontSheetSize", entry.Value);

			if (yaml.TryGetValue("CursorSheetSize", out entry))
				CursorSheetSize = FieldLoader.GetValue<int>("CursorSheetSize", entry.Value);
		}

		public void LoadCustomData(ObjectCreator oc)
		{
			foreach (var kv in yaml)
			{
				if (reservedModuleNames.Contains(kv.Key))
					continue;

				var t = oc.FindType(kv.Key);
				if (t == null || !typeof(IGlobalModData).IsAssignableFrom(t))
					throw new InvalidDataException($"`{kv.Key}` is not a valid mod manifest entry.");

				IGlobalModData module;
				var ctor = t.GetConstructor(new[] { typeof(MiniYaml) });
				if (ctor != null)
				{
					// Class has opted-in to DIY initialization
					module = (IGlobalModData)ctor.Invoke(new object[] { kv.Value });
				}
				else
				{
					// Automatically load the child nodes using FieldLoader
					module = oc.CreateObject<IGlobalModData>(kv.Key);
					FieldLoader.Load(module, kv.Value);
				}

				modules.Add(module);
			}

			customDataLoaded = true;
		}

		static string[] YamlList(Dictionary<string, MiniYaml> yaml, string key)
		{
			if (!yaml.TryGetValue(key, out var value))
				return Array.Empty<string>();

			return value.Nodes.Select(n => n.Key).ToArray();
		}

		static IReadOnlyDictionary<string, string> YamlDictionary(Dictionary<string, MiniYaml> yaml, string key)
		{
			if (!yaml.TryGetValue(key, out var value))
				return new Dictionary<string, string>();

			return value.ToDictionary(my => my.Value);
		}

		public bool Contains<T>() where T : IGlobalModData
		{
			return modules.Contains<T>();
		}

		/// <summary>Load a cached IGlobalModData instance.</summary>
		public T Get<T>() where T : IGlobalModData
		{
			if (!customDataLoaded)
				throw new InvalidOperationException("Attempted to call Manifest.Get() before loading custom data!");

			var module = modules.GetOrDefault<T>();

			// Lazily create the default values if not explicitly defined.
			if (module == null)
			{
				module = (T)Game.ModData.ObjectCreator.CreateBasic(typeof(T));
				modules.Add(module);
			}

			return module;
		}

		/// <summary>
		/// Load an uncached IGlobalModData instance directly from the manifest yaml.
		/// This should only be used by external mods that want to query data from this mod.
		/// </summary>
		public T Get<T>(ObjectCreator oc) where T : IGlobalModData
		{
			var t = typeof(T);
			if (!yaml.TryGetValue(t.Name, out var data))
			{
				// Lazily create the default values if not explicitly defined.
				return (T)oc.CreateBasic(t);
			}

			IGlobalModData module;
			var ctor = t.GetConstructor(new[] { typeof(MiniYaml) });
			if (ctor != null)
			{
				// Class has opted-in to DIY initialization
				module = (IGlobalModData)ctor.Invoke(new object[] { data.Value });
			}
			else
			{
				// Automatically load the child nodes using FieldLoader
				module = oc.CreateObject<IGlobalModData>(t.Name);
				FieldLoader.Load(module, data);
			}

			return (T)module;
		}

		public void Dispose()
		{
			foreach (var module in modules)
			{
				var disposableModule = module as IDisposable;
				disposableModule?.Dispose();
			}
		}
	}
}
