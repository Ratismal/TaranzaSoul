﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace TaranzaSoul
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient socketClient;
        private DiscordRestClient restClient;
        private Config config;
        private CommandHandler handler;
        private Logger logger;
        private DatabaseHelper dbhelper;
        private List<string> SpoilerWords = new List<string>();
        private Dictionary<string, ulong> RoleColors = new Dictionary<string, ulong>();
        private ulong updateChannel = 0;

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100
            });
            socketClient.Log += Log;

            restClient = new DiscordRestClient(new DiscordRestConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            restClient.Log += Log;

            if (File.Exists("./update"))
            {
                var temp = File.ReadAllText("./update");
                ulong.TryParse(temp, out updateChannel);
                File.Delete("./update");
                Console.WriteLine($"Found an update file! It contained [{temp}] and we got [{updateChannel}] from it!");
            }

            config = await Config.Load();

            dbhelper = new DatabaseHelper();
            logger = new Logger();

            var map = new ServiceCollection().AddSingleton(socketClient).AddSingleton(config).AddSingleton(logger).AddSingleton(dbhelper).BuildServiceProvider();
            
            await socketClient.LoginAsync(TokenType.Bot, config.Token);
            await socketClient.StartAsync();

            await restClient.LoginAsync(TokenType.Bot, config.Token);

            if (File.Exists("./deadlock"))
            {
                Console.WriteLine("We're recovering from a deadlock.");
                File.Delete("./deadlock");
                foreach (var u in config.OwnerIds)
                {
                    (await restClient.GetUserAsync(u))?
                        .SendMessageAsync($"I recovered from a deadlock.\n`{DateTime.Now.ToShortDateString()}` `{DateTime.Now.ToLongTimeString()}`");
                }
            }

            socketClient.GuildAvailable += Client_GuildAvailable;
            socketClient.Disconnected += SocketClient_Disconnected;

            await dbhelper.Install(map);
            await logger.Install(map);
            SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
            RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");

            handler = new CommandHandler();
            await handler.Install(map);

            try
            {
                socketClient.MessageReceived += Client_MessageReceived;
                socketClient.ReactionAdded += Client_ReactionAdded;
                socketClient.ReactionRemoved += Client_ReactionRemoved;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Source}\n{ex.Message}\n{ex.StackTrace}");
            }

            //var avatar = new Image(File.OpenRead(".\\TaranzaSOUL.png"));
            //await client.CurrentUser.ModifyAsync(x => x.Avatar = avatar);

            await Task.Delay(-1);
        }

        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (updateChannel != 0 && guild.GetTextChannel(updateChannel) != null)
            {
                await Task.Delay(3000); // wait 3 seconds just to ensure we can actually send it. this might not do anything.
                await guild.GetTextChannel(updateChannel).SendMessageAsync("Successfully reconnected.");
                updateChannel = 0;
            }
        }

        private async Task SocketClient_Disconnected(Exception ex)
        {
            // If we disconnect, wait 3 minutes and see if we regained the connection.
            // If we did, great, exit out and continue. If not, check again 3 minutes later
            // just to be safe, and restart to exit a deadlock.
            var task = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    await Task.Delay(1000 * 60 * 3);

                    if (socketClient.ConnectionState == ConnectionState.Connected)
                        break;
                    else if (i == 1)
                    {
                        File.Create("./deadlock");
                        await config.Save();
                        Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
                    }
                }
            });
        }

        private async Task Client_ReactionRemoved(Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                if (user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                    await user.RemoveRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
            }
        }

        private async Task Client_ReactionAdded(Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id) && x.Id != RoleColors[reaction.Emote.Name]))
                {
                    await user.RemoveRoleAsync(r);
                }

                if (!user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                    await user.AddRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
            }
            else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "🚫")
            {
                var user = ((SocketGuildUser)reaction.User);

                foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id)))
                {
                    await user.RemoveRoleAsync(r);
                }
            }
        }
        
        private async Task Client_MessageReceived(SocketMessage msg)
        {
            if (msg.Author.Id == 102528327251656704 && msg.Content.ToLower() == "<@267405866162978816> update colors")
            {
                RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");
                await msg.Channel.SendMessageAsync("Done!");
                return;
            }
            
            //if (msg.Author.Id == 267405866162978816) return;

            ////if ((msg.Channel as IGuildChannel) == null)
            ////    return;

            //if ((((IGuildUser)msg.Author).RoleIds.Contains((ulong)132721372848848896) ||
            //    (((IGuildUser)msg.Author).RoleIds.Contains((ulong)190657363798261769)))
            //    && msg.Content.ToLower() == "<@267405866162978816> get filter")
            //{
            //    await msg.Channel.SendFileAsync("@./filter.json", "Here you go.");
            //}

            //if ((((IGuildUser)msg.Author).RoleIds.Contains((ulong)132721372848848896) ||
            //    (((IGuildUser)msg.Author).RoleIds.Contains((ulong)190657363798261769)))
            //    && msg.Content.ToLower() == "<@267405866162978816> update filter")
            //{
            //    string file = "";

            //    if (msg.Attachments.Count() > 0)
            //    {
            //        if (msg.Attachments.FirstOrDefault().Filename.ToLower().EndsWith(".json"))
            //            file = msg.Attachments.FirstOrDefault().Url;
            //        else
            //        {
            //            await msg.Channel.SendMessageAsync("That isn't a .json file!");
            //            return;
            //        }
            //    }
            //    else
            //    {
            //        await msg.Channel.SendMessageAsync("I don't see any attachments!");
            //        return;
            //    }

            //    using (WebClient client = new WebClient())
            //    {
            //        await client.DownloadFileTaskAsync(new Uri(file), $"@./temp/{file}");
            //    }

            //    var tempWords = new List<string>();

            //    try
            //    {
            //        tempWords = JsonStorage.DeserializeObjectFromFile<List<string>>($"@./temp/{file}");
            //    }
            //    catch (Exception ex)
            //    {
            //        await msg.Channel.SendMessageAsync($"There was an error loading that file:\n{ex.Message}");
            //        return;
            //    }

            //    File.Delete("@./filter.json");
            //    File.Move($"@./temp/{file}", "@./filter.json");

            //    SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
            //    await msg.Channel.SendMessageAsync("Done!");
            //    return;
            //}

            //if (msg.Channel.Id == 417458111553470474 || msg.Channel.Id == 423578054775013377 ||
            //    msg.Channel.Id == 361589776433938432 || msg.Channel.Id == 425752341833187328 ||
            //    msg.Channel.Id == 429821654068101120 || msg.Channel.Id == 186342269274554368 ||
            //    msg.Channel.Id == 190674947381657600)
            //    return;

            //if (((IGuildChannel)msg.Channel).GuildId != 132720341058453504)
            //    return;

            //var tmp = msg.Content.ToLower();

            //if (msg.Content.ToLower().Split(' ').Any(x => SpoilerWords.Contains(x)))
            //{

            //}

            //foreach (var s in SpoilerWords)
            //{
            //    if (msg.Channel.Id == 268945818470449162 && s == "flamberge")
            //        continue;

            //    if (tmp.Contains(s))
            //    {

            //        if (!s.Contains(" "))
            //        {
            //            bool match = false;

            //            foreach (var word in tmp.Split(' '))
            //            {
            //                if (word == s)
            //                {
            //                    match = true;
            //                    break;
            //                }
            //            }

            //            if (!match) continue;
            //        }

            //        await Task.Delay(100);
            //        await msg.DeleteAsync();
            //        string send = $"{msg.Author.Mention} that's a late game spoiler! That belongs in <#417458111553470474>!";

            //        await msg.Channel.SendMessageAsync(send);
            //    }
            //}
        }
        

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
