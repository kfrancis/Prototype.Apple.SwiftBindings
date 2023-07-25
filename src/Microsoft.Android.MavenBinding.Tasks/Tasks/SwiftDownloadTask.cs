using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using MavenNet;
using MavenNet.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Prototype.Android.MavenBinding.Tasks;

namespace Prototype.Apple.SwiftBinding.Tasks
{
	public class SwiftDownloadTask : Task
	{
		/// <summary>
		/// The cache directory to use for Maven artifacts.
		/// </summary>
		[Required]
		public string SwiftCacheDirectory { get; set; } = null!; // NRT enforced by [Required]

		/// <summary>
		/// The set of input Maven libraries that we need to download.
		/// </summary>
		public ITaskItem []? AppleSwiftLibraries { get; set; }

		/// <summary>
		/// This is a hack for unit tests.
		/// </summary>
		public LogWrapper? Logger { get; set; }

		public override bool Execute ()
		{
			return ExecuteAsync ().GetAwaiter ().GetResult ();
		}

		async System.Threading.Tasks.Task<bool> ExecuteAsync ()
		{
			var log = Logger ??= new MSBuildLogWrapper (Log);

			// Download NuGet package list
			// TODO: Cache this better
			await TryDownloadNuGetPackageList (log);

			var resolved = new List<ITaskItem> ();

			foreach (var library in AppleSwiftLibraries.OrEmpty ()) {
				// Validate artifact
				var id = library.ItemSpec;
				var version = library.GetRequiredMetadata ("Version", log);
				if (version is null)
					continue;

				var artifact = SwiftExtensions.ParseArtifact (id, version, log);

				if (artifact is null)
					continue;

				// Create resolved TaskItem
				var result = new TaskItem (library.ItemSpec);

				// Check for local files
				if (TryGetLocalFiles (library, result, log)) {
					library.CopyMetadataTo (result);
					resolved.Add (result);
					continue;
				}

				// Check for repository files
				if (await TryGetRepositoryFiles (artifact, library, result, log)) {
					library.CopyMetadataTo (result);
					resolved.Add (result);
					continue;
				}
			}

			return !log.HasLoggedErrors;
		}

		async System.Threading.Tasks.Task<bool> TryGetRepositoryFiles (Artifact artifact, ITaskItem item, TaskItem result, LogWrapper log)
		{
			// Initialize repo
			var repository = GetRepository (item);

			if (repository is null)
				return false;

			artifact.SetRepository (repository);

			// Download artifact
			var artifact_file = await MavenExtensions.DownloadPayload (artifact, MavenCacheDirectory, log);

			if (artifact_file is null)
				return false;

			// Download POM
			var pom_file = await MavenExtensions.DownloadPom (artifact, MavenCacheDirectory, log);

			if (pom_file is null)
				return false;

			result.SetMetadata ("ArtifactFile", artifact_file);
			result.SetMetadata ("ArtifactPom", pom_file);

			return true;
		}

		MavenRepository? GetRepository (ITaskItem item)
		{
			var type = item.GetMetadataOrDefault ("Repository", "Central");

			var repo = type.ToLowerInvariant () switch {
				"central" => MavenRepository.FromMavenCentral (),
				"google" => MavenRepository.FromGoogle (),
				_ => (MavenRepository?) null
			};

			if (repo is null && type.StartsWith ("http", StringComparison.OrdinalIgnoreCase))
				repo = MavenRepository.FromUrl (type);

			if (repo is null)
				Log.LogError ("Unknown Maven repository: '{0}'.", type);

			return repo;
		}

		bool TryGetLocalFiles (ITaskItem item, TaskItem result, LogWrapper log)
		{
			var type = item.GetMetadataOrDefault ("Repository", "Central");

			if (type.ToLowerInvariant () == "file") {
				var artifact_file = item.GetMetadataOrDefault ("PackageFile", "");
				var pom_file = item.GetMetadataOrDefault ("PomFile", "");

				if (!artifact_file.HasValue () || !pom_file.HasValue ()) {
					log.LogError ("'PackageFile' and 'PomFile' must be specified when using a 'File' repository.");
					return false;
				}

				if (!File.Exists (artifact_file)) {
					log.LogError ("Specified package file '{0}' does not exist.", artifact_file);
					return false;
				}

				if (!File.Exists (pom_file)) {
					log.LogError ("Specified pom file '{0}' does not exist.", pom_file);
					return false;
				}

				result.SetMetadata ("ArtifactFile", artifact_file);
				result.SetMetadata ("ArtifactPom", pom_file);

				return true;
			}

			return false;
		}

		async System.Threading.Tasks.Task TryDownloadNuGetPackageList (LogWrapper log)
		{
			try {
				var http = new HttpClient ();

				var json = await http.GetStringAsync ("https://aka.ms/ms-nuget-packages");

				var outfile = Path.Combine (SwiftCacheDirectory, "microsoft-packages.json");

				File.WriteAllText (outfile, json);
			} catch (Exception ex) {
				log.LogMessage ("Could not download microsoft-packages.json: {0}", ex);
			}
		}
	}
}
