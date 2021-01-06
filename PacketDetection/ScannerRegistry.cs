﻿using FFXIVOpcodeWizard.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXIVOpcodeWizard.PacketDetection
{
    public class ScannerRegistry
    {
        private readonly IList<Scanner> scanners;

        public IList<Scanner> AsList() => scanners.ToList();

        public ScannerRegistry()
        {
            this.scanners = new List<Scanner>();
            DeclareScanners();
        }

        private void DeclareScanners()
        {
            //=================
            RegisterScanner("PlayerSetup", "Please log in.",
                PacketSource.Server,
                (packet, parameters) => packet.PacketSize > 300 && IncludesBytes(packet.Data, Encoding.UTF8.GetBytes(parameters[0])),
                new[] { "Please enter your character name:" });

            //=================
            var maxHp = 0;
            RegisterScanner("UpdateHpMpTp", "Please alter your HP or MP and allow your stats to regenerate completely.",
                PacketSource.Server,
                (packet, parameters) =>
                {
                    if (maxHp == 0)
                    {
                        maxHp = int.Parse(parameters[0]);
                    }

                    if (packet.PacketSize != 40 && packet.PacketSize != 48) return false;

                    var packetHp = BitConverter.ToUInt32(packet.Data, Offsets.IpcData);
                    var packetMp = BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4);

                    return packetHp == maxHp && packetMp == 10000;
                }, new[] { "Please enter your max HP:" });
            //=================
            RegisterScanner("PlayerStats", "Switch to another job, and then switch back.",
                PacketSource.Server, (packet, parameters) =>
                    packet.PacketSize == 256 && BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 24) == maxHp &&
                    BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 28) == 10000 && // MP equals 10000
                    BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 36) == 10000);  // GP equals 10000
            //=================
            RegisterScanner("UpdatePositionHandler", "Please move your character.",
                PacketSource.Client,
                (packet, _) => packet.PacketSize == 56 &&
                               packet.SourceActor == packet.TargetActor &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 4) == 0 &&
                               BitConverter.ToUInt64(packet.Data, Offsets.IpcData + 8) != 0 &&
                               BitConverter.ToUInt32(packet.Data, packet.Data.Length - 4) == 0);
            //=================
            RegisterScanner("ClientTrigger", "Please draw your weapon.",
                PacketSource.Client,
                (packet, _) =>
                    packet.PacketSize == 64 && BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == 1);
            RegisterScanner("ActorControl", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 56 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 4) == 1);
            //=================
            RegisterScanner("ActorControlSelf", "Please enter sanctuary and wait for rested bonus gains.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 64 &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData) == 24 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 4) <= 604800 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8) == 0 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 12) == 0 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 16) == 0 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 20) == 0 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 24) == 0);
            //=================
            RegisterScanner("ActorControlTarget", "Please mark yourself with the \"1\" marker.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 64 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x04) == 0 &&
                               packet.SourceActor == packet.TargetActor &&
                               packet.SourceActor == BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x08) &&
                               packet.SourceActor == BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x18));
            //=================
            RegisterScanner("ChatHandler", "Please /say your message in-game:",
                PacketSource.Client,
                (packet, parameters) => IncludesBytes(packet.Data, Encoding.UTF8.GetBytes(parameters[0])),
                new[] { "Please enter a message to /say in-game:" });
            //=================
            RegisterScanner("Playtime", "Please type /playtime.",
                PacketSource.Server,
                (packet, parameters) => {
                    if (packet.PacketSize != 40 || packet.SourceActor != packet.TargetActor) return false;

                    var playtime = BitConverter.ToUInt32(packet.Data, (int)Offsets.IpcData);

                    var inputDays = int.Parse(parameters[0]);
                    var packetDays = playtime / 60 / 24;

                    // In case you played for 23:59:59
                    return inputDays == packetDays || inputDays + 1 == packetDays;
                }, new[] { "Type /playtime, and input the days you played:" });
            //=================
            byte[] searchBytes = null;
            RegisterScanner("SetSearchInfoHandler", "Please set that search comment in-game.",
                PacketSource.Client,
                (packet, parameters) =>
                {
                    searchBytes ??= Encoding.UTF8.GetBytes(parameters[0]);
                    return IncludesBytes(packet.Data, searchBytes);
                }, new[] { "Please enter a somewhat lengthy search message here, before entering it in-game:" });
            RegisterScanner("UpdateSearchInfo", string.Empty,
                PacketSource.Server,
                (packet, _) => IncludesBytes(packet.Data, searchBytes));
            RegisterScanner("ExamineSearchInfo", "Open your search information with the \"View Search Info\" button.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize > 232 && IncludesBytes(packet.Data, searchBytes));
            //=================
            RegisterScanner("Examine", "Please examine that character's equipment.",
                PacketSource.Server,
                (packet, parameters) => packet.PacketSize == 1016 && IncludesBytes(packet.Data, Encoding.UTF8.GetBytes(parameters[0])),
                new[] { "Please enter a nearby character's name:" });
            //=================
            const int marketBoardItemDetectionId = 17837; // Grade 7 Dark Matter
            RegisterScanner("MarketBoardSearchResult", "Please click \"Catalysts\" on the market board.",
                PacketSource.Server,
                (packet, _) => {
                    if (packet.PacketSize != 208) return false;

                    for (var i = 0; i < 22; ++i)
                    {
                        var itemId = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8 * i);
                        if (itemId == 0)
                        {
                            break;
                        }

                        if (itemId == marketBoardItemDetectionId)
                        {
                            return true;
                        }
                    }

                    return false;
                });
            RegisterScanner("MarketBoardItemListingCount", "Please open the market board listings for Grade 7 Dark Matter.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == marketBoardItemDetectionId);
            RegisterScanner("MarketBoardItemListingHistory", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 1080 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == marketBoardItemDetectionId);
            RegisterScanner("MarketBoardItemListing", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize > 1552 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 44) == marketBoardItemDetectionId);
            //=================
            RegisterScanner("ActorMove", "Please teleport to Limsa Lominsa Lower Decks and wait.",
                PacketSource.Server,
                (packet, _) =>
                {
                    if (packet.PacketSize != 48) return false;

                    var x = (float)BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 6) / 65536 * 2000 - 1000;
                    var y = (float)BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) / 65536 * 2000 - 1000;
                    var z = (float)BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 12) / 65536 * 2000 - 1000;
                    return Math.Abs(x + 85) < 15 && Math.Abs(z - 0) < 15 && Math.Abs(y - 19) < 2;
                }
            );
            //=================
            RegisterScanner("MarketTaxRates", "Please visit a retainer counter and request information about market tax rates.",
                PacketSource.Server,
                (packet, _) =>
                {
                    if (packet.PacketSize != 72)
                        return false;

                    var rate1 = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8);
                    var rate2 = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 12);
                    var rate3 = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 16);
                    var rate4 = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 20);

                    return rate1 <= 7 && rate2 <= 7 && rate3 <= 7 && rate4 <= 7;
                });
            //=================
            byte[] retainerBytes = null;
            RegisterScanner("RetainerInformation", "Please use the Summoning Bell.",
                PacketSource.Server,
                (packet, parameters) =>
                {
                    retainerBytes ??= Encoding.UTF8.GetBytes(parameters[0]);
                    return packet.PacketSize == 112 && IncludesBytes(packet.Data.Skip(73).Take(32).ToArray(), retainerBytes);
                }, new[] { "Please enter one of your retainers' names:" });
            //=================
            RegisterScanner("NpcSpawn", "Please summon that retainer.",
                PacketSource.Server,
                (packet, parameters) => packet.PacketSize > 624 &&
                    IncludesBytes(packet.Data.Skip(588).Take(36).ToArray(), retainerBytes));
            //=================
            RegisterScanner("PlayerSpawn", "Please wait for another player to spawn in your vicinity.",
                PacketSource.Server, (packet, parameters) =>
                    packet.PacketSize > 500 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4) ==
                    int.Parse(parameters[0]), new[] { "Please enter your world ID:" });
            RegisterScanner("ActorFreeSpawn", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 40 &&
                               packet.SourceActor == packet.TargetActor);
            //=================
            RegisterScanner("ItemInfo", "Please teleport and open your chocobo saddlebag.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 96 &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 4000);
            //=================
            RegisterScanner("UpdateClassInfo",
                "Please switch to the job you entered a level for:",
                PacketSource.Server, (packet, parameters) =>
                    packet.PacketSize == 48 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4) ==
                    int.Parse(parameters[0]), new[] { "Please enter the level of the job you can switch to:" });
            //=================
            var lightningCrystals = -1;
            RegisterScanner("ActorCast", "Please teleport to New Gridania.",
                PacketSource.Server,
                (packet, parameters) =>
                {
                    if (lightningCrystals == -1) lightningCrystals = int.Parse(parameters[0]);
                    return packet.PacketSize == 64 &&
                           BitConverter.ToUInt16(packet.Data, Offsets.IpcData) == 5;
                },
                new[] { "Please enter the number of Lightning Crystals you have:" });
            RegisterScanner("CurrencyCrystalInfo", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 64 &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4) == 2001 &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 6) == 10 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8) == lightningCrystals &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 16) == 12);
            RegisterScanner("InitZone", string.Empty, PacketSource.Server,
                (packet, _) => packet.PacketSize == 128 &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 2) == 132);
            //=================
            RegisterScanner("EffectResult", "Please use Sprint while at full HP and MP.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 120 &&
                               packet.SourceActor == packet.TargetActor &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 4) == packet.SourceActor &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8) ==
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 12) &&
                               BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 16) == 10000 &&
                               packet.Data[Offsets.IpcData + 21] > 0
                ); // We don't actually check for the Sprint effect because this is enough, but we can if necessary in the future.
            //=================
            RegisterScanner("EventStart", "Please begin fishing and put your rod away immediately.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 56 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8) == 0x150001);
            RegisterScanner("EventPlay", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 72 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 8) == 0x150001);
            RegisterScanner("EventFinish", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == 0x150001 &&
                               packet.Data[Offsets.IpcData + 4] == 0x14 &&
                               packet.Data[Offsets.IpcData + 5] == 0x01);
            //=================
            RegisterScanner("SomeDirectorUnk4", "Please cast your line and catch a fish.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 56 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x08) == 257);
            RegisterScanner("EventPlay4", string.Empty,
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 80 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x1C) == 284);
            //=================
            RegisterScanner("UpdateInventorySlot", "Please purchase a Pill Bug to use as bait.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 96 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x10) == 2587);
            //=================
            uint inventoryModifyHandlerId = 0;
            RegisterScanner("InventoryModifyHandler", "Please drop the Pill Bug.",
                PacketSource.Client,
                (packet, _, comment) => {
                    var match = packet.PacketSize == 80 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 0x18) == 2587;
                    if (!match) return false;

                    inventoryModifyHandlerId = BitConverter.ToUInt32(packet.Data, Offsets.IpcData);

                    var baseOffset = BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4);
                    comment.Text = $"Base offset: {Util.NumberToString(baseOffset, NumberDisplayFormat.HexadecimalUppercase)}";
                    return true;
                });
            RegisterScanner("InventoryActionAck", "Please wait.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 && BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == inventoryModifyHandlerId);
            RegisterScanner("InventoryTransaction", "Please wait.",
                PacketSource.Server,
                (packet, _) => {
                    var match = packet.PacketSize == 80 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 0x18) == 2587;
                    if (!match) return false;

                    inventoryModifyHandlerId = BitConverter.ToUInt32(packet.Data, Offsets.IpcData);
                    return true;
                });
            RegisterScanner("InventoryTransactionFinish", "Please wait.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 && BitConverter.ToUInt32(packet.Data, Offsets.IpcData) == inventoryModifyHandlerId);
            //=================
            RegisterScanner("CFPreferredRole", "Please wait, this may take some time...", PacketSource.Server, (packet, _) =>
            {
                if (packet.PacketSize != 48)
                    return false;

                var allInRange = true;

                for (var i = 1; i < 10; i++)
                    if (packet.Data[Offsets.IpcData + i] > 4 || packet.Data[Offsets.IpcData + i] < 1)
                        allInRange = false;

                return allInRange;
            });
            //=================
            RegisterScanner("CFNotify", "Please enter the \"The Vault\" as an undersized party.", // CFNotifyPop
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 64 && packet.Data[Offsets.IpcData + 20] == 0x22);
            //=================
            RegisterScanner("UpdatePositionInstance", "Please move your character in an/the instance.",
                PacketSource.Client,
                (packet, _) => packet.PacketSize == 72 &&
                               packet.SourceActor == packet.TargetActor &&
                               BitConverter.ToUInt64(packet.Data, Offsets.IpcData) != 0 &&
                               BitConverter.ToUInt64(packet.Data, Offsets.IpcData + 0x08) != 0 &&
                               BitConverter.ToUInt64(packet.Data, Offsets.IpcData + 0x10) != 0 &&
                               BitConverter.ToUInt64(packet.Data, Offsets.IpcData + 0x18) != 0 &&
                               BitConverter.ToUInt32(packet.Data, packet.Data.Length - 4) == 0);
            //=================
            RegisterScanner("PrepareZoning", "Please find an Aethernet Shard and teleport to Lancers' Guild.",
                PacketSource.Server,
                (packet, _) =>
                {
                    if (packet.PacketSize != 48) return false;

                    var logMessage = BitConverter.ToUInt32(packet.Data, Offsets.IpcData);
                    var targetZone = BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 4);
                    var animation = BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 6);
                    var fadeOutTime = packet.Data[Offsets.IpcData + 10];

                    return logMessage == 0 &&
                           targetZone == 133 &&
                           animation == 112 &&
                           fadeOutTime == 14 &&
                           packet.SourceActor == packet.TargetActor;
                });
            //=================
            RegisterScanner("ActorSetPos", "Please teleport to Mih Khetto's Amphitheatre via the Aethernet Shard.",
                PacketSource.Server,
                (packet, _) =>
                {
                    if (packet.PacketSize != 56) return false;

                    var x = BitConverter.ToSingle(packet.Data, Offsets.IpcData + 8);
                    var y = BitConverter.ToSingle(packet.Data, Offsets.IpcData + 12);
                    var z = BitConverter.ToSingle(packet.Data, Offsets.IpcData + 16);

                    return Math.Abs(x + 75) < 15 && Math.Abs(z + 140) < 15 && Math.Abs(y - 7) < 2;
                }
            );
            //=================
            RegisterScanner("PlaceFieldMarker", "Please target the Mih Khetto's Amphitheatre Aethernet Shard and type /waymark A <t>",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 &&
                    packet.SourceActor == packet.TargetActor &&
                    BitConverter.ToUInt16(packet.Data, Offsets.IpcData) == 256 &&
                    BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x08) == 7237);
            //=================
            RegisterScanner("PlaceFieldMarkerPreset", "Please type /waymark clear",
                PacketSource.Server,
                (packet, _) => 
                {
                    if (packet.PacketSize != 136 || packet.SourceActor != packet.TargetActor) return false;

                    for (var i = 0; i < 24; i++)
                    {
                        if (BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 0x04 + 4 * i) != 0) return false;
                    }

                    return true;
                });
            //=================
            RegisterScanner("ObjectSpawn", "Please enter a furnished house.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 96 &&
                               packet.Data[Offsets.IpcData + 1] == 12 &&
                               packet.Data[Offsets.IpcData + 2] == 4 &&
                               packet.Data[Offsets.IpcData + 3] == 0 &&
                               BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 12) == 0);
            //=================
            RegisterScanner("LoadFurniture", "Please enter a furnished house and exit.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 2448 &&
                               packet.Data[Offsets.IpcData] == 0xFF &&
                               packet.Data[Offsets.IpcData + 7] == 0xFF);
            //=================
            RegisterScanner("MoveFurniture", "Please move a furniture to ground.",
                PacketSource.Client,
                (packet, _) =>
                {
                    if (packet.PacketSize != 64) return false;
                    var y = BitConverter.ToSingle(packet.Data, Offsets.IpcData + 16);
                    return Math.Abs(y) < 0.01 || Math.Abs(y + 7) < 0.01;
                });
            //=================
            RegisterScanner("CheckResident", "Please check the resident list at crystal.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 2440 &&
                               (packet.Data[Offsets.IpcData + 4] == 0x53 ||
                               packet.Data[Offsets.IpcData + 4] == 0x54 ||
                               packet.Data[Offsets.IpcData + 4] == 0x55 ||
                               packet.Data[Offsets.IpcData + 4] == 0x81));
            //=================
            RegisterScanner("CrossWorldChat", "Please wait for another player to say something in your world.",
                PacketSource.Server, (packet, parameters) =>
                    packet.PacketSize == 1104 && BitConverter.ToInt16(packet.Data, Offsets.IpcData + 12) ==
                    int.Parse(parameters[0]), new[] { "Please enter another player's world ID:" });
            //=================
            RegisterScanner("Effect", "Switch to White Mage, and cast Glare on an enemy. Then wait for a damage tick.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 156 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 16533);
            //=================
            RegisterScanner("AddStatusEffect", "Please use Dia.",
                PacketSource.Server,
                (packet, _) => 
                    packet.PacketSize == 128 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 30) == 1871 || 
                    packet.PacketSize == 120 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 26) == 1871);
            //=================
            RegisterScanner("StatusEffectList", "Please wait...",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 416 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 20) == 1871);
            //=================
            RegisterScanner("ActorGauge", "Wait for gauge changes, then clear the lilies.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 48 &&
                    packet.Data[Offsets.IpcData] == 24 &&
                    packet.Data[Offsets.IpcData + 5] == 0 &&
                    packet.Data[Offsets.IpcData + 6] > 0);
            //=================
            RegisterScanner("AoeEffect8", "Attack multiple enemies with Holy.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 668 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 139);
            //=================
            RegisterScanner("AoeEffect16", "Attack multiple enemies (>8) with Holy.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 1244 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 139);
            //=================
            RegisterScanner("AoeEffect24", "Attack multiple enemies (>16) with Holy.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 1820 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 139);
            //=================
            RegisterScanner("AoeEffect32", "Attack multiple enemies (>24) with Holy.",
                PacketSource.Server,
                (packet, _) => packet.PacketSize == 2396 && BitConverter.ToUInt16(packet.Data, Offsets.IpcData + 8) == 139);
            //=================
            RegisterScanner("MiniCactpotInit", "Start playing Mini Cactpot.",
                PacketSource.Server,
                (packet, _) =>
                {
                    if (packet.Data.Length != Offsets.IpcData + 136) return false;

                    var indexEnd = packet.Data[Offsets.IpcData + 7];
                    var column = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 12);
                    var row = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 16);
                    var digit = BitConverter.ToUInt32(packet.Data, Offsets.IpcData + 20);

                    return indexEnd == 23 &&
                           column <= 2 &&
                           row <= 2 &&
                           digit <= 9;
                });
        }

        /// <summary>
        /// Adds a scanner to the scanner registry.
        /// </summary>
        /// <param name="packetName">The name (Sapphire-style) of the packet.</param>
        /// <param name="tutorial">How the packet's conditions are created.</param>
        /// <param name="source">Whether the packet originates on the client or the server.</param>
        /// <param name="del">A boolean function that returns true if a packet matches the contained heuristics.</param>
        /// <param name="paramPrompts">An array of requests for auxiliary data that will be passed into the detection delegate.</param>
        private void RegisterScanner(
            string packetName,
            string tutorial,
            PacketSource source,
            Func<IpcPacket, string[], Comment, bool> del,
            string[] paramPrompts = null)
        {
            this.scanners.Add(new Scanner
            {
                PacketName = packetName,
                Tutorial = tutorial,
                ScanDelegate = del,
                Comment = new Comment(),
                ParameterPrompts = paramPrompts ?? new string[] { },
                PacketSource = source,
            });
        }

        private void RegisterScanner(
            string packetName,
            string tutorial,
            PacketSource source,
            Func<IpcPacket, string[], bool> del,
            string[] paramPrompts = null)
        {
            bool Fn(IpcPacket a, string[] b, Comment c) => del(a, b);
            RegisterScanner(packetName, tutorial, source, Fn, paramPrompts);
        }

        private static bool IncludesBytes(byte[] source, byte[] search)
        {
            if (search == null) return false;

            for (var i = 0; i < source.Length - search.Length; ++i)
            {
                var result = true;
                for (var j = 0; j < search.Length; ++j)
                {
                    if (search[j] != source[i + j])
                    {
                        result = false;
                        break;
                    }
                }

                if (result)
                {
                    return true;
                }
            }

            return false;
        }
    }
}