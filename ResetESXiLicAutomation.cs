using Renci.SshNet;
using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

public class ResetESXiLicAutomation
{
    static readonly string user = "root";
    static readonly string password = "";
    static readonly string hostsFile = "esxihosts.txt";

    //Add new ESXi host here ...
    static string[] ESXiHosts =
    {
        "192.168.32.160",
        "192.168.32.161",
        "192.168.32.162",
        "192.168.32.163",
        "192.168.32.164",
        "192.168.32.165",
        "192.168.32.166",
        "192.168.32.167",
        "192.168.32.168"
    };

    //Write to log file and console
    public static void Wtf(String line = "")
    {
        StreamWriter sw;
        
        string logfile = "logfile.txt";

        using (sw = new StreamWriter(logfile, true))
        {
            if (!string.IsNullOrEmpty(line))
            {
                sw.WriteLine(DateTime.Now + " -> " + line);
                Console.WriteLine(DateTime.Now + " -> " + line);
            }
            else
            {
                sw.WriteLine();
                Console.WriteLine();
            }
        }
    }

    //Event handler for password prompt
    public static void HandleKeyEvent(Object sender, Renci.SshNet.Common.AuthenticationPromptEventArgs e)
    {
        foreach (Renci.SshNet.Common.AuthenticationPrompt prompt in e.Prompts)
        {
            if (prompt.Request.IndexOf("Password:", StringComparison.InvariantCultureIgnoreCase) != -1)
            {
                prompt.Response = password;
            }
        }
    }

    //Start SSH service using PowerCLI (assuming it is installed)
    private static bool EnableSSHOnHost(string ESXIP, string user = "", string pwd = "")
    {

        bool result = false;
        string psCmd1 = @"Import-Module VMware.VimAutomation.Core";
        string psCmd2 = string.Format("Connect-VIServer {0} -user {1} -Password {2}", ESXIP, user, pwd);
        string psCmd3 = @"get-vmhostservice | where {$_.Key -eq ""TSM-SSH""} | Start-VMHostService";
        string psCmd4 = @"Disconnect-VIServer * -Confirm:$false";

        string scriptBlock = psCmd1 + "\r\n" + psCmd2 + "\r\n" + psCmd3 + "\r\n" + psCmd4;

        try
        {
            RunspaceInvoke ri = new RunspaceInvoke();
            var riResult = ri.Invoke(scriptBlock);
            if (riResult != null) result = true;

        }
        catch 
        {
            Wtf("Host not responding to PowerCLI. Giving up since host is probably switched off!");
            Wtf();
        }

        return result;
    }

    //Reset ESXi evaluation license via SSH
    private static void ResetLicense(SshClient sshclient)
    {
        //Connect to SSH if connection was dropped
        if (!sshclient.IsConnected) { sshclient.Connect(); }

        //Remove current license
        sshclient.RunCommand("rm -r /etc/vmware/license.cfg");

        //Copy new license
        sshclient.RunCommand("cp /etc/vmware/.#license.cfg /etc/vmware/license.cfg");

        //Restart service
        sshclient.RunCommand("/etc/init.d/vpxa restart");

        //Disconnect from ESXi
        sshclient.Disconnect();

        Wtf("License has been reset!");
        Wtf();
    }

    //Resets the license on an ESXi host to the default 60 day trial period (Jason 5/4/18)
    public static void ResetLicence(string ESXIP)
    {
        try
        {
            //If IP address is provided ...
            if (!string.IsNullOrEmpty(ESXIP))
            {
                Wtf("Resetting password on ESXi host " + ESXIP);

                KeyboardInteractiveAuthenticationMethod kauth = new KeyboardInteractiveAuthenticationMethod(user);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(user, password);

                kauth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);
                ConnectionInfo connectionInfo = new ConnectionInfo(ESXIP, user, pauth, kauth);

                var client = new SshClient(connectionInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(45)
                };

                //Connect to ESXi
                Wtf("Connecting to host ...");

                try
                {
                    client.Connect();
                    ResetLicense(client);
                }
                catch
                {
                    Wtf("Could not establish a connection. Let's try and enable SSH via PowerCLI!");
                    if (EnableSSHOnHost(ESXIP, user, password))
                    {
                        ResetLicense(client);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Wtf("Failed to reset ESXi license. " + e.Message);
        }
    }

    public static void Main(string[] args)
    {
        try
        {
            if (File.Exists(hostsFile))
            {
                int i = 0;
                Array.Clear(ESXiHosts, 0, ESXiHosts.Length);

                using (StreamReader sr = new StreamReader(hostsFile))
                {
                    string hostip;
                    while ((hostip = sr.ReadLine()) != null)
                    {
                        ESXiHosts[i] = hostip;
                        i++;
                    }
                }
            }

            foreach (string host in ESXiHosts)
            {
                ResetLicence(host);
            }
        }
        catch (Exception e)
        {
            Wtf(e.Message);
        }
    }
}