using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MavenNet.Models;
using Prototype.Android.MavenBinding.Tasks;

namespace Prototype.Apple.SwiftBinding.Tasks
{
	static class SwiftExtensions
	{
		public static Artifact? ParseArtifact (string id, string version, LogWrapper log)
		{
			var parts = id.Split (new [] { ':' }, StringSplitOptions.RemoveEmptyEntries);

			if (parts.Length != 2 || parts.Any (p => string.IsNullOrWhiteSpace (p))) {
				log.LogError ("Artifact specification '{0}' is invalid.", id);
				return null;
			}

			var artifact = new Artifact (parts [1], parts [0], version);

			return artifact;
		}
	}
}
