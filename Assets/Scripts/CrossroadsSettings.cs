using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using Microsoft.Win32;
using System;
using SFB;

public class CrossroadsSettings : MonoBehaviour
{
    //Find us at #modding here
    //Hollow Knight Discord: https://discord.gg/hollowknight

    [Header("Set to false to keep created files after leaving play mode")]
    public bool removeCreatedFoldersInEditorMode = true;

    //TODO: make these input fields and update the config file when these are changed
    public Text gamePathLabel;
    public Text localRepoLabel;
    public Text modsFolderLabel;

    public bool Loaded { get; private set; }

    //functions to run after loading is complete
    public UnityEngine.Events.UnityEvent OnLoaded;

    bool debugSkipHollowKnightFolderFinder = false;

    bool foundGamePath = false;

    string settingsFolderName = "Settings";
    public string SettingsFolderPath
    {
        get
        {
            return UnityEngine.Application.dataPath + "/"+ settingsFolderName + "/";
        }
    }

    string settingsFileName = "settings.xml";
    public string SettingsFilePath
    {
        get
        {
            return SettingsFolderPath + settingsFileName;
        }
    }
    
    //folder for downloaded mod files
    string localModRepoFolderName = "DownloadedMods";
    public string LocalModRepoFolderPath
    {
        get
        {
            //if(!string.IsNullOrEmpty(localModRepoFolderPath))
            //    return localModRepoFolderPath;
            return UnityEngine.Application.dataPath + "/" + localModRepoFolderName;
        }
    }

    string defaultModInstallFolderName = "hollow_knight_Data/Managed/Mods/";

    string defaultGameFolderName = "Hollow Knight";

    public string BackupPath {
        get {
            if( Settings == null )
                return "Settings not loaded";
            return Settings.gamePath + "/Backup";
        }
    }

    public string ReadmePath {
        get {
            if( Settings == null )
                return "Settings not loaded";
            return Settings.gamePath + "/Readme";
        }
    }

    [XmlRoot("AppSettings")]
    public class AppSettings
    {
        [XmlElement("GamePath")]
        public string gamePath;
        [XmlElement("ModsInstallPath")]
        public string modsInstallPath;
        //[XmlElement("LocalModRepoPath")]
        //public string modRepoPath;

        [XmlArray("InstalledMods")]
        public List<ModSettings> installedMods;

        [XmlElement(ElementName ="IsGOG", IsNullable = true)]
        public bool? isGOG;
    }

    public AppSettings Settings { get; private set; }
    
    //file finder, use to find the hollow knight folder when first creating the settings file
    FileFinder finder = new FileFinder();

    IEnumerator Start()
    {
        Loaded = false;
        
        if( UnityEngine.Application.dataPath.Contains( "Temp" ) )
        {
            System.Windows.Forms.MessageBox.Show( "Crossroads will not run from inside a windows Temp directory. Please extract this program into another location." );
            UnityEngine.Application.Quit();
            yield break;
        }


        AppSettings appSettings = new AppSettings();
        if( !ReadSettingsFromFile( out appSettings ) )
        {
            System.Windows.Forms.MessageBox.Show( "Failed to read settings file. " );
            UnityEngine.Application.Quit();
        }
        else
        {
            if( File.Exists( appSettings.gamePath + "/hollow_knight.exe" ) || File.Exists( appSettings.gamePath + "/hollow_knight.x86_64" ) )
            {
                foundGamePath = true;
            }
            else
            {
                if( UnityEngine.Application.isEditor )
                    Debug.LogError( "Warning: Did not find hollow_knight.exe at " + appSettings.gamePath );
                else
                    System.Windows.Forms.MessageBox.Show( "Warning: Did not find hollow_knight.exe at " + appSettings.gamePath +@". Please find the 'Hollow Knight' directory and select it." );
                
                SelectHollowKnightExe();
                appSettings = Settings;
            }
        }
        
        //if the finder has started, don't load yet
        if( finder.Running || !foundGamePath )
        {
            //Error???? Though we may have already errored by now so this may be reduntant
        }
        
        LoadSettings( appSettings );
    }

    public void AddInstalledModInfo( ModSettings modSettings )
    {
        Settings.installedMods.Add( modSettings );
        WriteSettingsToFile( Settings );
    }

    public void RemoveInstalledModInfo(string modName)
    {
        Settings.installedMods = Settings.installedMods.Select(x => x).Where(x => x.modName != modName).ToList();
        WriteSettingsToFile(Settings);
    }

    public void SetIsGOG(bool newState)
    {
        Settings.isGOG = newState;
        WriteSettingsToFile( Settings );
    }

    public ModSettings GetInstalledModByName(string modname)
    {
        if( Settings == null )
            return null;

        foreach(ModSettings ms in Settings.installedMods)
        {
            if(ms.modName == modname)
            {
                return ms;
            }
        }

        return null;
    }

    IEnumerator SetupDefaults()
    {
        if(!Directory.Exists(SettingsFolderPath))
            Directory.CreateDirectory(SettingsFolderPath);

        if(!File.Exists(SettingsFilePath))
        {
            CreateDefaultSettings();

            //use the debug skip while testing
            if(debugSkipHollowKnightFolderFinder)
            {
                Settings.gamePath = SettingsFolderPath;
                Settings.modsInstallPath = SettingsFolderPath + defaultModInstallFolderName;
                if(!Directory.Exists(Settings.modsInstallPath))
                    Directory.CreateDirectory(Settings.modsInstallPath);
                WriteSettingsToFile(Settings);
            }
            else
            {
                //try using the registery to locate steam and then using steamd game directory first
                yield return TryRegisterySteamSearch();

                if( foundGamePath )
                    yield break;
                
                //if that doesn't work, try running the brute force finder

                //for now, let's not do this as it seems to break on some PCs
                //TODO: figure out how to get this working
                //finder.OnFindCompleteCallback = WriteFoundGamePath;

                //yield return finder.ThreadedFind(defaultGameFolderName);
            }
        }

        yield break;
    }
    
    IEnumerator TryRegisterySteamSearch()
    {
        object value = null;

        try
        {
            value = Registry.CurrentUser.OpenSubKey( "Software" ).OpenSubKey( "Valve" ).OpenSubKey( "Steam" ).GetValue( "SteamPath" );
        }
        catch(Exception e)
        {
            Debug.LogError( "Cannot find steam! Skipping the registry detection step... Error message: "+e.Message );
        }

        if( value == null )
            yield break;

        //search local steam directories first
        string localSteamPath = (value as string) + "/steamapps/common";
        Debug.Log( localSteamPath );
        if( Directory.Exists( localSteamPath ) )
        {
            foreach( string s in Directory.EnumerateDirectories( localSteamPath ) )
            {
                Debug.Log( s );
                if( s.Contains( "Hollow Knight" ) )
                {
                    WriteFoundGamePath( s );
                    yield break;
                }
                yield return null;
            }
        }

        //now search any alternate directories
        string steamConfigPath = (value as string) + "/Config/config.vdf";

        //something is horribly wrong in the universe
        if( !File.Exists( steamConfigPath ) )
        {
            Debug.LogError( "Cannot find config at "+ steamConfigPath );
            yield break;
        }

        //BaseInstallFolder

        int counter = 0;
        string line;
        Debug.Log( "parsing config" );

            // Read the file and display it line by line.  
        System.IO.StreamReader file = new System.IO.StreamReader(steamConfigPath);
        while( ( line = file.ReadLine() ) != null )
        {
            if(line.Contains( "BaseInstallFolder"))
            {
                int startIndex = line.IndexOf("_");

                startIndex = line.IndexOf('\"',startIndex+3) + 1;

                int endIndex = line.LastIndexOf('\"');

                int length = endIndex - startIndex;

                string gamesPath = line.Substring(startIndex,length);

                gamesPath = gamesPath.Replace(@"\\",@"\");

                Debug.Log( gamesPath );

                foreach(string s in Directory.EnumerateDirectories( gamesPath ))
                {
                    Debug.Log( s );

                    if( s.Contains( "Hollow Knight" ) )
                    {
                        WriteFoundGamePath( s );
                        yield break;
                    }

                    if( s.Contains( "SteamApps" ) )
                    {
                        foreach( string a in Directory.EnumerateDirectories( gamesPath, "Hollow Knight", SearchOption.AllDirectories ) )
                        {
                            if( a.Contains( "Hollow Knight" ) )
                            {
                                WriteFoundGamePath( a );
                                yield break;
                            }
                            yield return null;
                        }
                    }
                    yield return null;
                }
            }
            counter++;

            //TEST FOR NOW (update/remove me)
            if( counter > 200000 )
                yield break;

            if( counter % 1000 == 0 )
                yield return null;
        }

        yield break;
    }

    void CreateDefaultSettings()
    {
        AppSettings defaultSettings = new AppSettings
        {
            gamePath = "Path Not Set",
            modsInstallPath = "Path Not Set",
            //modRepoPath = LocalModRepoFolderPath,
            installedMods = new List<ModSettings>()
        };
        Settings = defaultSettings;
        WriteSettingsToFile(Settings);
    }

    void WriteSettingsToFile(AppSettings settings)
    {
        if(!Directory.Exists(SettingsFolderPath))
            Directory.CreateDirectory(SettingsFolderPath);
        XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
        FileStream fstream = null;
        try
        {
            fstream = new FileStream(SettingsFilePath, FileMode.Create);
            serializer.Serialize(fstream, settings);
        }
        catch(System.Exception e)
        {
            System.Windows.Forms.MessageBox.Show("Error creating/saving settings file "+ e.Message);
        }
        finally
        {
            fstream.Close();
        }
    }

    bool ReadSettingsFromFile(out AppSettings settings)
    {
        settings = null;

        if(!File.Exists( SettingsFilePath ) )
        {
            System.Windows.Forms.MessageBox.Show("No settings file found at " + SettingsFilePath + ". If this is your first time running Crossroads this is normal." );
            CreateDefaultSettings();
            return ReadSettingsFromFile(out settings);
        }

        bool returnResult = true;

        XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
        FileStream fstream = null;
        try
        {
            fstream = new FileStream(SettingsFilePath, FileMode.Open);
            settings = serializer.Deserialize(fstream) as AppSettings;
        }
        catch(System.Exception e)
        {
            System.Windows.Forms.MessageBox.Show("Error loading settings file " + e.Message);
            returnResult = false;
        }
        finally
        {
            fstream.Close();
        }

        return returnResult;
    }

    void WriteFoundGamePath(string path)
    {
        foundGamePath = true;

        //Debug.Log( Settings.gamePath );
        Settings.gamePath = path;
        //Debug.Log( Settings.gamePath );
        Settings.modsInstallPath = path + "/" + defaultModInstallFolderName;

        if(!Directory.Exists(Settings.modsInstallPath))
            Directory.CreateDirectory(Settings.modsInstallPath);

        WriteSettingsToFile(Settings);
        finder.OnFindCompleteCallback = null;
    }

    void LoadSettings(AppSettings settings)
    {
        Debug.Log( "trying to load settings" );
        if( Loaded )
            return;

        if(settings == null)
            return;

        Settings = settings;

        //create the folder to store downloaded mods
        if( !Directory.Exists( LocalModRepoFolderPath ) )
            Directory.CreateDirectory( LocalModRepoFolderPath );
        gamePathLabel.text = Settings.gamePath;
        localRepoLabel.text = LocalModRepoFolderPath;
        modsFolderLabel.text = Settings.modsInstallPath;
        Loaded = true;
        if(OnLoaded != null)
            OnLoaded.Invoke();
    }
    bool getHollowKnightExeLinux(string startingPath)
    {
        var dlg = new OpenFileDialog()
        {
            Title            = "Choose your Hollow Knight executable",
            AddExtension     = true,
            CheckFileExists  = true,
            CheckPathExists  = true,
            InitialDirectory = startingPath,
            Multiselect      = false
        };
        if (dlg.ShowDialog() == DialogResult.OK && dlg.FileNames.Length > 0)
        {
            string checkPath = Path.GetDirectoryName(dlg.FileName);
            Debug.Log( checkPath );
            if( Directory.Exists(checkPath) && (File.Exists( checkPath + "/hollow_knight.exe" ) || File.Exists( checkPath + "/hollow_knight.x86_64" )))
            {
                Settings.gamePath = checkPath;
                Settings.modsInstallPath = checkPath + "/" + defaultModInstallFolderName;
                WriteFoundGamePath( checkPath );
                // The directory searching window does not disappear on Linux unless you replace it with a message box. Yes it is stupid I agree. This is a dumb hack to make the window slightly smaller by using a tiny message box instead of a large directory window.
                System.Windows.Forms.MessageBox.Show( "Hollow Knight found!" );
            }
            else
            {
                System.Windows.Forms.MessageBox.Show( "Hollow Knight not found in "+checkPath );
                foundGamePath = false;
            }
        } else {
            foundGamePath = false;
        }
    return foundGamePath;
    }


    public void SelectHollowKnightExe()
    {
        if( finder.Running )
            finder.CancelSearch();

        string startingPath = @"C:\Program Files (x86)";
        // If program files doesn't exist, start in the user's home folder instead.
        string[] possibleStartingPaths = {
        @"C:\Program Files (x86)",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        foreach (string p in possibleStartingPaths)
        {
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
            {
                startingPath = p;
                break;
            }
        }

        #if UNITY_STANDALONE_LINUX
        foundGamePath = getHollowKnightExeLinux(startingPath);
        #else
        var paths = StandaloneFileBrowser.OpenFolderPanel( "Select your Hollow Knight game folder", startingPath, false);

        foreach( var s in paths )
            Debug.Log( s );

        if( paths.Length > 0 )
        {
            string checkPath = paths[0];

            if( Directory.Exists(checkPath) && File.Exists( checkPath + "/hollow_knight.exe" ) )
            {
                Settings.gamePath = checkPath;
                Settings.modsInstallPath = checkPath + "\\" + defaultModInstallFolderName;
                WriteFoundGamePath( checkPath );
            }
            else
            {
                System.Windows.Forms.MessageBox.Show( "hollow_knight.exe not found in "+checkPath );
                foundGamePath = false;
            }
        }
        else
        {
            foundGamePath = false;
        }
        #endif
    }

    //Cleanup install folders on quit in editor mode
    void OnApplicationQuit()
    {
        if( UnityEngine.Application.isEditor && removeCreatedFoldersInEditorMode )
        {
            if( Directory.Exists( UnityEngine.Application.dataPath + "/" + "Settings" + "/" ) )
                Directory.Delete( UnityEngine.Application.dataPath + "/" + "Settings" + "/", true );

            if( Directory.Exists( UnityEngine.Application.dataPath + "/" + "DownloadedMods" + "/" ) )
                Directory.Delete( UnityEngine.Application.dataPath + "/" + "DownloadedMods" + "/", true );            
        }
    }
}
