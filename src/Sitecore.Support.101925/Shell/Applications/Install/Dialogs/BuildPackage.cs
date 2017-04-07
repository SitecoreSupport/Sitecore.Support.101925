using Sitecore.Data.LanguageFallback;
using Sitecore.Data.Proxies;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Install;
using Sitecore.Install.Framework;
using Sitecore.Install.Serialization;
using Sitecore.IO;
using Sitecore.Jobs.AsyncUI;
using Sitecore.Shell.Applications.Install;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Pages;
using Sitecore.Web.UI.Sheer;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Sitecore.Support.Shell.Applications.Install.Dialogs
{
    public class BuildPackage : WizardForm
    {
        [Serializable]
        private class AsyncHelper
        {
            private string packageFile;

            private string solutionFile;

            public AsyncHelper(string solutionFile, string packageFile)
            {
                this.solutionFile = solutionFile;
                this.packageFile = packageFile;
            }

            public void Generate()
            {
                try
                {
                    using (new ProxyDisabler())
                    {
                        using (new LanguageFallbackItemSwitcher(new bool?(false)))
                        {
                            PackageGenerator.GeneratePackage(this.solutionFile, this.packageFile, new SimpleProcessingContext());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Package generation failed: " + ex.ToString(), this);
                    JobContext.SendMessage("build:failed(message=" + ex.Message + ")");
                    JobContext.Flush();
                    throw ex;
                }
            }
        }

        protected Border FailureMessage;

        protected JobMonitor Monitor;

        protected Edit PackageFile;

        protected Border SuccessMessage;

        public string FileName
        {
            get
            {
                return StringUtil.GetString(Context.ClientPage.ServerProperties["FileName"]);
            }
            set
            {
                Context.ClientPage.ServerProperties["FileName"] = value;
            }
        }

        public string ResultFile
        {
            get
            {
                return StringUtil.GetString(Context.ClientPage.ServerProperties["ResultFile"]);
            }
            set
            {
                Context.ClientPage.ServerProperties["ResultFile"] = value;
            }
        }

        protected override void ActivePageChanged(string page, string oldPage)
        {
            base.ActivePageChanged(page, oldPage);
            if (page == "Building")
            {
                this.BackButton.Disabled = true;
                this.NextButton.Disabled = true;
                this.CancelButton.Disabled = true;
                Context.ClientPage.SendMessage(this, "buildpackage:generate");
            }
            if (page == "LastPage")
            {
                this.BackButton.Disabled = true;
            }
        }

        protected override bool ActivePageChanging(string page, ref string newpage)
        {
            bool result;
            if (page == "SetName" && newpage == "Building")
            {
                if (this.PackageFile.Value.Trim().Length == 0 || this.PackageFile.Value.IndexOfAny(Path.GetInvalidPathChars()) != -1)
                {
                    Context.ClientPage.ClientResponse.Alert(Translate.Text("Enter a valid name for the package."));
                    Context.ClientPage.ClientResponse.Focus(this.PackageFile.ID);
                    result = false;
                    return result;
                }
                string fullPackagePath;
                try
                {
                    fullPackagePath = ApplicationContext.GetFullPackagePath(Installer.GetFilename(this.PackageFile.Value.Trim()));
                    Path.GetDirectoryName(fullPackagePath);
                }
                catch (Exception ex)
                {
                    Log.Error("Noncritical: " + ex.ToString(), this);
                    Context.ClientPage.ClientResponse.Alert(Translate.Text("Entered name could not be resolved into an absolute file path.") + Environment.NewLine + Translate.Text("Enter a valid name for the package."));
                    Context.ClientPage.ClientResponse.Focus(this.PackageFile.ID);
                    result = false;
                    return result;
                }
                if (File.Exists(fullPackagePath) && !MainUtil.GetBool(Context.ClientPage.ServerProperties["__NameConfirmed"], false))
                {
                    Context.ClientPage.Start(this, "AskOverwrite");
                    result = false;
                    return result;
                }
                Context.ClientPage.ServerProperties.Remove("__NameConfirmed");
            }
            result = base.ActivePageChanging(page, ref newpage);
            return result;
        }

        public void AskOverwrite(ClientPipelineArgs args)
        {
            if (!args.IsPostBack)
            {
                Context.ClientPage.ClientResponse.Confirm(Translate.Text("File exists. Do you wish to overwrite?"));
                args.WaitForPostBack();
            }
            else if (args.HasResult && args.Result == "yes")
            {
                Context.ClientPage.ClientResponse.SetDialogValue(args.Result);
                Context.ClientPage.ServerProperties["__NameConfirmed"] = true;
                base.Next();
            }
        }

        [HandleMessage("buildpackage:download")]
        protected void DownloadPackage(Message message)
        {
            string resultFile = this.ResultFile;
            if (resultFile.Length > 0)
            {
                Context.ClientPage.ClientResponse.Download(resultFile);
            }
            else
            {
                Context.ClientPage.ClientResponse.Alert("Could not download package");
            }
        }

        private string GeneratePackageFileName(PackageProject project)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(PackageUtils.CleanupFileName(project.Metadata.PackageName));
            if (project.Metadata.Version.Length > 0)
            {
                stringBuilder.Append("-");
                stringBuilder.Append(project.Metadata.Version);
            }
            if (stringBuilder.Length == 0)
            {
                stringBuilder.Append(Sitecore.Install.Constants.UnnamedPackage);
            }
            stringBuilder.Append(".zip");
            return stringBuilder.ToString();
        }

        private void Monitor_Finished(object sender, EventArgs e)
        {
            base.Next();
        }

        [HandleMessage("build:failed")]
        protected void OnBuildFailed(Message message)
        {
            string @string = StringUtil.GetString(new string[]
            {
                message["message"]
            });
            Context.ClientPage.ClientResponse.SetStyle("SuccessMessage", "display", "none");
            Context.ClientPage.ClientResponse.SetStyle("FailureMessage", "display", "");
            Context.ClientPage.ClientResponse.SetInnerHtml("FailureMessage", Translate.Text("Package generation failed: {0}.", new object[]
            {
                @string
            }));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!Context.ClientPage.IsEvent)
            {
                this.FileName = StringUtil.GetString(new string[]
                {
                    Context.Request.QueryString["source"]
                });
                PackageProject packageProject = IOUtils.LoadSolution(FileUtil.ReadFromFile(MainUtil.MapPath(this.FileName)));
                if (packageProject != null)
                {
                    this.PackageFile.Value = this.GeneratePackageFileName(packageProject);
                }
                if (this.Monitor == null)
                {
                    this.Monitor = new JobMonitor();
                    this.Monitor.ID = "Monitor";
                    Context.ClientPage.Controls.Add(this.Monitor);
                }
            }
            else if (this.Monitor == null)
            {
                this.Monitor = (Context.ClientPage.FindControl("Monitor") as JobMonitor);
            }
            this.Monitor.JobFinished += new EventHandler(this.Monitor_Finished);
            this.Monitor.JobDisappeared += new EventHandler(this.Monitor_Finished);
        }

        [HandleMessage("buildpackage:generate")]
        protected void StartPackage(Message message)
        {
            string text = Installer.GetFilename(this.PackageFile.Value);
            if (string.Compare(Path.GetExtension(text), Sitecore.Install.Constants.PackageExtension, StringComparison.InvariantCultureIgnoreCase) != 0)
            {
                text += Sitecore.Install.Constants.PackageExtension;
            }
            this.ResultFile = text;
            this.StartTask(this.FileName, text);
        }

        private void StartTask(string solutionFile, string packageFile)
        {
            this.Monitor.Start("BuildPackage", "PackageDesigner", new ThreadStart(new BuildPackage.AsyncHelper(solutionFile, packageFile).Generate));
        }
    }
}