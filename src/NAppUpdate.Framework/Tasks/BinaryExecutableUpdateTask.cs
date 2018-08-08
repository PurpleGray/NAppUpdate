using System;
using System.IO;
using System.Text;
using NAppUpdate.Framework.Common;
using NAppUpdate.Framework.Conditions;
using NAppUpdate.Framework.Sources;
using NAppUpdate.Framework.Utils;

namespace NAppUpdate.Framework.Tasks
{
	[Serializable]
	[UpdateTaskAlias("binaryUpdate")]
	public class BinaryExecutableUpdateTask : UpdateTaskBase
	{
		[NauField("localPath", "The local path of the file to update", true)]
		public string LocalPath { get; set; }

		[NauField("updateTo",
			"File name on the remote location; same name as local path will be used if left blank"
			, false)]
		public string UpdateTo { get; set; }

		[NauField("sha256-checksum", "SHA-256 checksum to validate the file after download (optional)", false)]
		public string Sha256Checksum { get; set; }

		private string _destinationFile, _backupFile, _tempFile;

		public override void Prepare(IUpdateSource source)
		{
			if (string.IsNullOrEmpty(LocalPath))
			{
				UpdateManager.Instance.Logger.Log(Logger.SeverityLevel.Warning, "FileUpdateTask: LocalPath is empty, task is a noop");
				return; // Errorneous case, but there's nothing to prepare to, and by default we prefer a noop over an error
			}

			string fileName;
			if (!string.IsNullOrEmpty(UpdateTo))
			{
				fileName = UpdateTo;
			}
			else
			{
				fileName = LocalPath;
			}

			_tempFile = null;

			string baseUrl = UpdateManager.Instance.BaseUrl;
			string tempFileLocal = Path.Combine(UpdateManager.Instance.Config.TempFolder, Guid.NewGuid().ToString());

			UpdateManager.Instance.Logger.Log("FileUpdateTask: Downloading {0} with BaseUrl of {1} to {2}", fileName, baseUrl, tempFileLocal);

			if (!source.GetData(fileName, baseUrl, OnProgress, ref tempFileLocal))
				throw new UpdateProcessFailedException("FileUpdateTask: Failed to get file from source");

			_tempFile = tempFileLocal;
			if (_tempFile == null)
				throw new UpdateProcessFailedException("FileUpdateTask: Failed to get file from source");

			if (!string.IsNullOrEmpty(Sha256Checksum))
			{
				string checksum = FileChecksum.GetSHA256Checksum(_tempFile);
				if (!checksum.Equals(Sha256Checksum))
					throw new UpdateProcessFailedException(string.Format("FileUpdateTask: Checksums do not match; expected {0} but got {1}", Sha256Checksum, checksum));
			}

			_destinationFile = Path.Combine(Path.GetDirectoryName(UpdateManager.Instance.ApplicationPath), LocalPath);
			UpdateManager.Instance.Logger.Log("FileUpdateTask: Prepared successfully; destination file: {0}", _destinationFile);
		}

		public override TaskExecutionStatus Execute(bool coldRun)
		{
			if (string.IsNullOrEmpty(LocalPath))
			{
				UpdateManager.Instance.Logger.Log(Logger.SeverityLevel.Warning, "FileUpdateTask: LocalPath is empty, task is a noop");
				return TaskExecutionStatus.Successful; // Errorneous case, but there's nothing to prepare to, and by default we prefer a noop over an error
			}

			var dirName = Path.GetDirectoryName(_destinationFile);
			if (!Directory.Exists(dirName))
			{
				Utils.FileSystem.CreateDirectoryStructure(dirName, false);
			}

			// Create a backup copy if target exists
			if (_backupFile == null && File.Exists(_destinationFile))
			{
				if (!Directory.Exists(Path.GetDirectoryName(Path.Combine(UpdateManager.Instance.Config.BackupFolder, LocalPath))))
				{
					string backupPath = Path.GetDirectoryName(Path.Combine(UpdateManager.Instance.Config.BackupFolder, LocalPath));
					Utils.FileSystem.CreateDirectoryStructure(backupPath, false);
				}
				_backupFile = Path.Combine(UpdateManager.Instance.Config.BackupFolder, LocalPath);
				File.Copy(_destinationFile, _backupFile, true);
			}

			// Create bat file which will serve as updater after application will be closed
			var destDir = Path.GetDirectoryName(_destinationFile);

			if (!File.Exists(Path.Combine(destDir, "restart.bat")))
			{
				File.WriteAllText("restart.bat", "sleep 5 \r\n", Encoding.UTF8);
			}

			File.AppendAllText("restart.bat", $"xcopy /x /y {_backupFile} {_destinationFile} \r\n");

			return TaskExecutionStatus.Successful;
		}

		public override bool Rollback()
		{
			throw new NotImplementedException();
		}
	}
}
