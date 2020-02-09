using Newtonsoft.Json;
using Reddit;
using Reddit.AuthTokenRetriever;
using Reddit.Controllers;
using Reddit.Controllers.EventArgs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace RedditBerner
{
    public class Workflow
    {
        private string ConfigDir { get; set; }
        private string ConfigPath { get; set; }
        private string SubredditsPath { get; set; }
        private string ScriptsDir { get; set; }

        private Config Config { get; set; }
        private IList<string> Scripts { get; set; }
        private IList<Subreddit> Subreddits { get; set; }
        private Random Random { get; set; }

        private RedditClient Reddit { get; set; }
        public bool Active { get; set; }

        private string AppId { get; set; } = "z8huXvY0aph0PQ";

        public Workflow()
        {
            ConfigDir = Path.Combine(Environment.CurrentDirectory, "config");
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            ConfigPath = Path.Combine(ConfigDir, "RedditBerner.config.json");
            if (!File.Exists(ConfigPath))
            {
                // Create new config file and prompt user for token retrieval process.  --Kris
                Config = new Config(AppId);

                Console.WriteLine("****************************");
                Console.WriteLine("* Welcome to RedditBerner! *");
                Console.WriteLine("****************************");

                Console.WriteLine();

                Console.WriteLine("Before the bot can run, we'll need to link it to your Reddit account.");
                Console.WriteLine("This is very easy:  Whenever you're ready, press any key and a browser window will open and take you to the Reddit authentication page.");
                Console.WriteLine("Enter your username/password if you're not already logged in, then scroll down and click on the 'Allow' button to authorize this app to use your Reddit account.");

                Console.WriteLine();

                Console.WriteLine("Press any key to continue....");

                Console.ReadKey();

                AuthTokenRetrieverLib authTokenRetrieverLib = new AuthTokenRetrieverLib(AppId);
                authTokenRetrieverLib.AwaitCallback();

                Console.Clear();

                Console.WriteLine("Opening web browser....");

                OpenBrowser(authTokenRetrieverLib.AuthURL());

                DateTime start = DateTime.Now;
                while (string.IsNullOrWhiteSpace(authTokenRetrieverLib.RefreshToken)
                    && start.AddMinutes(5) > DateTime.Now)
                {
                    Thread.Sleep(1000);
                }

                if (string.IsNullOrWhiteSpace(authTokenRetrieverLib.RefreshToken))
                {
                    throw new Exception("Unable to authorize Reddit; timeout waiting for Refresh Token.");
                }

                Config.AccessToken = authTokenRetrieverLib.AccessToken;
                Config.RefreshToken = authTokenRetrieverLib.RefreshToken;

                SaveConfig();

                Console.WriteLine("Reddit authentication successful!  Press any key to continue....");

                Console.ReadKey();
            }
            else
            {
                Console.WriteLine("*************************");
                Console.WriteLine("*      RedditBerner     *");
                Console.WriteLine("* Created by Kris Craig *");
                Console.WriteLine("*************************");

                Console.WriteLine();

                LoadConfig();
            }

            ScriptsDir = Path.Combine(Environment.CurrentDirectory, "scripts");
            if (!Directory.Exists(ScriptsDir))
            {
                Directory.CreateDirectory(ScriptsDir);
            }

            if (!Directory.EnumerateFileSystemEntries(ScriptsDir).Any())
            {
                throw new Exception("Scripts directory cannot be empty!  Please add at least 1 text file to serve as a comment template so the app knows what content to post.");
            }

            LoadScripts();
            if (Scripts == null
                || Scripts.Count.Equals(0))
            {
                throw new Exception("No suitable scripts found!  Please add at least 1 text file under 10 K to serve as a comment template so the app knows what content to post.");
            }

            Reddit = new RedditClient(appId: AppId, refreshToken: Config.RefreshToken, accessToken: Config.AccessToken);

            SubredditsPath = Path.Combine(ConfigDir, "subreddits.json");
            if (!File.Exists(SubredditsPath))
            {
                Subreddits = new List<Subreddit>
                {
                    Reddit.Subreddit("StillSandersForPres"),
                    Reddit.Subreddit("WayOfTheBern"),
                    Reddit.Subreddit("SandersForPresident"),
                    Reddit.Subreddit("BernieSanders")
                };
                SaveSubreddits();
            }
            else
            {
                LoadSubreddits();
            }

            Random = new Random();
        }

        public void Start()
        {
            Log("Commencing bot workflow....");

            // Begin monitoring.  --Kris
            MonitorStart();

            Active = true;
            while (Active)
            {
                Thread.Sleep(3000);
            }

            // End monitoring.  --Kris
            MonitorEnd();

            Log("Bot workflow terminated.");
        }

        private void MonitorStart()
        {
            foreach (Subreddit subreddit in Subreddits)
            {
                Log("Monitoring " + subreddit.Name + " for new posts....");

                subreddit.Posts.GetNew();
                subreddit.Posts.MonitorNew();
                subreddit.Posts.NewUpdated += C_NewPostsUpdated;
            }
        }

        private void MonitorEnd()
        {
            foreach (Subreddit subreddit in Subreddits)
            {
                Log("Terminating monitoring of " + subreddit.Name + " for new posts....");

                subreddit.Posts.NewUpdated -= C_NewPostsUpdated;
                subreddit.Posts.MonitorNew();
            }
        }

        private void LoadConfig()
        {
            Log("Loading config....");

            Config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath));

            Log("Config loaded successfully.");
        }

        private void SaveConfig()
        {
            Log("Saving config....");

            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(Config));

            Log("Config saved successfully.");
        }

        private void LoadSubreddits()
        {
            Log("Loading subreddits....");

            IList<string> subs = JsonConvert.DeserializeObject<IList<string>>(File.ReadAllText(SubredditsPath));

            Subreddits = new List<Subreddit>();
            foreach (string sub in subs)
            {
                Subreddits.Add(Reddit.Subreddit(sub));
                Log("Loaded " + sub + " successfully.");
            }
        }

        private void SaveSubreddits()
        {
            Log("Saving subreddits list....");

            IList<string> subs = new List<string>();
            foreach (Subreddit subreddit in Subreddits)
            {
                subs.Add(subreddit.Name);
            }

            File.WriteAllText(SubredditsPath, JsonConvert.SerializeObject(subs));

            Log("Subreddits list saved successfully.");
        }

        // Note - Script files must end in a .txt extension and not exceed 10,000 characters in length in order to be recognized.  --Kris
        private void LoadScripts()
        {
            Log("Loading comment scripts....");

            Scripts = new List<string>();
            foreach (FileInfo file in (new DirectoryInfo(ScriptsDir)).GetFiles("*.txt", SearchOption.AllDirectories))
            {
                if (file.Length <= 10000)
                {
                    Scripts.Add(File.ReadAllText(file.FullName));
                    Log("Loaded script file '" + file.FullName + "' successfully.");
                }
            }
        }

        public void OpenBrowser(string authUrl, string browserPath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(authUrl);
                Process.Start(processStartInfo);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // This typically occurs if the runtime doesn't know where your browser is.  Use BrowserPath for when this happens.  --Kris
                ProcessStartInfo processStartInfo = new ProcessStartInfo(browserPath)
                {
                    Arguments = authUrl
                };
                Process.Start(processStartInfo);
            }
        }

        // Replaces "{subreddit}" with sub name and "{post}" with post fullname (ex. "?sub={subreddit}&post={post}" might become "?sub=WayOfTheBern&post=t3_d0vw1j").  --Kris
        private string ParseVars(string s, Post post)
        {
            return s.Replace(@"{subreddit}", post.Subreddit).Replace(@"{postid}", post.Id).Replace(@"{post}", post.Fullname);
        }

        private void Log(string message, string subreddit = null)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + (!string.IsNullOrWhiteSpace(subreddit) ? "[" + subreddit + "] " : "") + message);
        }

        // Fires asynchronously whenever a new post is detected in one of the monitored subreddits.  --Kris
        public void C_NewPostsUpdated(object sender, PostsUpdateEventArgs e)
        {
            // Comment on each new post as it comes in.  --Kris
            foreach (Post post in e.Added)
            {
                Comment comment = post.Comment(ParseVars(Scripts[Random.Next(0, Scripts.Count)], post)).Submit();
                Log("Added comment " + comment.Fullname + " to post " + post.Fullname);
            }
        }
    }
}
