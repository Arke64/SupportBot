using ChatSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SupportBot {
    public class Program {
        private readonly IReadOnlyDictionary<string, Action<IrcChannel, string>> handlers;
        private readonly Random random;
        private readonly List<string> channels;
        private readonly IDictionary<string, string[]> tellLines;
        private IrcUser user;
        private IrcClient client;
        private string nick;
        private string server;
        private bool started;

        public static void Main() => new Program().RunCommandLoop();

        public Program() {
            this.handlers = new Dictionary<string, Action<IrcChannel, string>> {
                ["roll"] = this.OnRollReceived,
                ["tell"] = this.OnTellReceived
            };
            this.random = new Random((int)DateTime.UtcNow.Ticks);
            this.channels = new List<string>();
            this.tellLines = new Dictionary<string, string[]>();
        }

        private void Start() {
            this.user = new IrcUser(this.nick, this.nick);
            this.client = new IrcClient(this.server, this.user);
            this.client.ConnectionComplete += (s, e) => this.channels.ForEach(c => this.client.JoinChannel(c));
            this.client.ChannelMessageRecieved += this.OnMessageReceived;
            this.client.Error += (s, e) => this.OnError(e.Error.ToString());
            this.client.NetworkError += (s, e) => this.OnError(e.SocketError.ToString());
            this.client.ConnectAsync();
            this.started = true;
        }

        private void OnError(string error) {
            Console.WriteLine("Error: " + error);

            Environment.Exit(1);
        }

        private void OnMessageReceived(object sender, ChatSharp.Events.PrivateMessageEventArgs e) {
            lock (this) {
                var channel = this.client.Channels[e.PrivateMessage.Source];
                var message = e.PrivateMessage.Message;
                var idx = message.IndexOf(this.nick + " ");

                if (idx == 0) {
                    message = message.Substring(this.nick.Length + 1);

                    var commandEnd = message.IndexOf(' ');

                    if (commandEnd != -1 && this.handlers.TryGetValue(message.Substring(0, commandEnd).ToLowerInvariant(), out var handler))
                        handler(channel, message.Substring(commandEnd + 1));
                }
            }
        }

        private void OnRollReceived(IrcChannel channel, string command) {
            foreach (var roll in command.Split(' ')) {
                var parts = roll.Split('d');

                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], out var rolls)) continue;

                var mods = parts[1].Split('+');

                if (mods.Length > 2) continue;

                if (!int.TryParse(mods[0], out var max)) continue;
                if (!int.TryParse(mods.Length == 2 ? mods[1] : "1", out var add)) continue;

                channel.SendMessage(string.Join(" ", Enumerable.Range(0, rolls).Select(i => this.random.Next(add, add + max).ToString("N0"))));
            }
        }

        private void OnTellReceived(IrcChannel channel, string command) {
            if (this.tellLines.TryGetValue(command, out var selected))
                channel.SendMessage(selected[this.random.Next(selected.Length)]);
        }

        private void RunCommandLoop() {
            Console.WriteLine("Enter commands as desired. Enter help for command information.");

            while (true) {
                Console.Write("> ");

                var parts = Console.ReadLine().Split(' ');

                lock (this) {
                    switch (parts[0].ToLowerInvariant()) {
                        case "nick" when parts.Length == 2:
                            this.nick = parts[1];

                            if (this.started)
                                this.client.Nick(this.nick);

                            break;

                        case "server" when parts.Length == 2:
                            if (this.started)
                                this.client.Quit("Shutting down.");

                            this.server = parts[1];

                            if (this.started)
                                this.Start();

                            break;

                        case "join" when !this.channels.Contains(parts[1]):
                            this.channels.Add(parts[1]);

                            if (this.started)
                                this.client.JoinChannel(parts[1]);

                            break;

                        case "set-lines" when parts.Length == 2:
                            this.tellLines[parts[1].ToLowerInvariant()] = File.ReadAllLines(parts[1] + ".txt");

                            break;

                        case "speak" when this.started && parts.Length == 2:
                            if (this.user.Channels.Contains(parts[1])) {
                                Console.Write("Message: ");

                                this.user.Channels[parts[1]].SendMessage(Console.ReadLine());
                            }
                            else {
                                Console.WriteLine("Not in channel.");
                            }

                            break;

                        case "channels":
                            Console.WriteLine("Channels: " + string.Join(";", this.channels));

                            break;

                        case "start" when !this.started:
                            if (this.nick != null && this.server != null) {
                                this.Start();
                            }
                            else {
                                Console.WriteLine("Info not set.");
                            }

                            break;

                        case "exit":
                            this.client?.Quit("Shutting down.");
                            this.client = null;

                            return;

                        case "help":
                            Console.WriteLine("nick [nick]: Sets or changes the nick to [nick].");
                            Console.WriteLine("server [server]: Sets or changes the server to [server].");
                            Console.WriteLine("join [channel]: Joins [channel].");
                            Console.WriteLine("set-lines [filename]: Add the lines in [file] to the tell command. Do not provide the extension; the file must exist in the same directory as this program.");
                            Console.WriteLine("speak [channel]: Say in [channel] the message specified after this command.");
                            Console.WriteLine("channels: Lists all channels in.");
                            Console.WriteLine("start: Connects to the server.");
                            Console.WriteLine("exit: Stops the program.");

                            break;

                        default:
                            Console.WriteLine("Invalid command.");

                            break;
                    }
                }
            }
        }
    }
}
