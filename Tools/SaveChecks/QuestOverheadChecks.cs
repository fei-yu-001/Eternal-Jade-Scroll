using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace TwelveJade.Core
{
    static class QuestOverheadChecks
    {
        static readonly System.Text.Json.JsonSerializerOptions Options=new System.Text.Json.JsonSerializerOptions{IncludeFields=true};
        static string Snapshot(SaveData s)=>System.Text.Json.JsonSerializer.Serialize(s,Options);
        static void Check(string label,bool pass){if(!pass)throw new Exception("FAIL: [QuestOverhead] "+label);Console.WriteLine("PASS: [QuestOverhead] "+label);}
        sealed class Codec:IJsonCodec
        {
            public string Serialize<T>(T value)=>System.Text.Json.JsonSerializer.Serialize(value,Options);
            public T Deserialize<T>(string text) where T:class=>System.Text.Json.JsonSerializer.Deserialize<T>(text,Options);
        }
        public static void Run(ItemTable items,string configDir,TownMap town,string tempRoot)
        {
            const string id="quest-herbal-rescue",npc="npc-yao-lao";
            var table=QuestTable.Parse(File.ReadAllText(Path.Combine(configDir,"quests.json")));var quest=table.Find(id);
            table.CrossCheckNpcs(town);
            Check("real giver and turn-in metadata",quest.GiverNpc==npc && quest.TurnInNpc==npc);
            var repository=new SaveRepository(Path.Combine(tempRoot,"overhead"),new Codec()){Items=items};
            var save=repository.Create(1,"farmer","核验");var before=Snapshot(save);
            Check("new save available marker",QuestRuntime.OverheadState(save,table,npc)==QuestOverheadState.Available);
            Check("unknown NPC no marker",QuestRuntime.OverheadState(save,table,"missing")==QuestOverheadState.None);
            Check("display query cannot add quest records",Snapshot(save)==before);
            var tree=DialogueTree.Parse(File.ReadAllText(Path.Combine(configDir,"dialogues.json")));
            var intro=tree.Find("yaolao-intro");var question=intro.Options.Single(o=>o.NextNode=="yaolao-rescue");
            var confirmation=tree.Find("yaolao-rescue").Options.Single(o=>o.NextNode=="yaolao-accept");
            var rejection=tree.Find("yaolao-rescue").Options.Single(o=>o.NextNode=="yaolao-bye");
            Check("initial question and refusal do not accept",tree.ActionOf(intro.Id,question)=="" && tree.ActionOf("yaolao-rescue",rejection)=="");
            Check("confirmation alone carries accept action",tree.ActionOf("yaolao-rescue",confirmation)=="acceptQuest" && tree.AcceptQuestOf("yaolao-rescue",confirmation)==id);
            Check("availability condition is read only",DialogueCondition.Satisfies(save,npc,0,"questAvailable:"+id,table) && Snapshot(save)==before);
            Check("unaccepted turn-in refused without mutation",QuestRuntime.TryTurnIn(save,table,items,id,npc,0).Completed.Count==0 && Snapshot(save)==before);
            QuestLedger.RefreshAvailability(save,table);Check("accept real task",QuestLedger.TryStart(save,quest));
            Check("active marker grey question",QuestRuntime.OverheadState(save,table,npc)==QuestOverheadState.InProgress);
            before=Snapshot(save);Check("incomplete refused without mutation",QuestRuntime.TryTurnIn(save,table,items,id,npc,100).Completed.Count==0 && Snapshot(save)==before);
            foreach(var objective in quest.Objectives)QuestLedger.Advance(save,quest,objective.Id,Math.Max(1,objective.Count));
            int count=InventoryRules.Count(save.bag,"jinchuangyao");
            var auto=QuestRuntime.Reconcile(save,table,items,QuestSignal.None());
            Check("completed objectives await explicit turn-in",auto.Completed.Count==0 && QuestLedger.StatusOf(save,id)==QuestLedger.StatusActive && InventoryRules.Count(save.bag,"jinchuangyao")==count);
            before=Snapshot(save);Check("gold question is a read-only derived state",QuestRuntime.OverheadState(save,table,npc)==QuestOverheadState.Completable && Snapshot(save)==before);
            Check("completed-condition replaced by completable condition",DialogueCondition.Satisfies(save,npc,0,"questCompletable:"+id,table) && !DialogueCondition.Satisfies(save,npc,0,"questDone:"+id,table));
            var handin=intro.Options.Single(o=>o.NextNode=="yaolao-thanks");
            Check("delivery action carries real task ID",tree.ActionOf(intro.Id,handin)=="turnInQuest" && tree.MetaOf(intro.Id,handin).TurnInQuest==id);
            foreach(float distance in new[]{-1,200.01f,float.NaN,float.PositiveInfinity})
                Check("invalid distance rejected "+distance,QuestRuntime.TryTurnIn(save,table,items,id,npc,distance).Completed.Count==0 && Snapshot(save)==before);
            Check("wrong NPC rejected without mutation",QuestRuntime.TryTurnIn(save,table,items,id,"npc-shitou",20).Completed.Count==0 && Snapshot(save)==before);
            var award=QuestRuntime.TryTurnIn(save,table,items,id,npc,200);
            Check("200-unit real hand-in gives exactly three",award.Completed.SequenceEqual(new[]{id}) && award.ItemsGranted==3 && InventoryRules.Count(save.bag,"jinchuangyao")==count+3);
            Check("thanks agrees with real reward",tree.Find("yaolao-thanks").Text.Contains("三帖都交给你") && quest.Rewards.Items.Single().count==3);
            Check("completed flag and marker disappear",save.HasFlag("herb-boy-saved") && QuestRuntime.OverheadState(save,table,npc)==QuestOverheadState.None);
            before=Snapshot(save);Check("double click does not mutate or award",QuestRuntime.TryTurnIn(save,table,items,id,npc,20).Completed.Count==0 && Snapshot(save)==before);
            repository.Write(save);var reloaded=repository.Read(1).Data;before=Snapshot(reloaded);
            Check("completed save remains completed after reload",QuestLedger.IsDone(reloaded,id));
            Check("reloaded hand-in cannot award again",QuestRuntime.TryTurnIn(reloaded,table,items,id,npc,20).Completed.Count==0 && Snapshot(reloaded)==before);
            var legacy=new QuestDef("legacy","旧任务",1,QuestLedger.StatusAvailable,new List<QuestObjective>(),new List<string>(),new QuestReward(7,new[]{new ItemStack("jinchuangyao",1)},new[]{"legacy-paid"}),"",npc);
            var legacyTable=new QuestTable(new List<QuestDef>{legacy});var legacySave=repository.Create(2,"farmer","旧档");QuestLedger.TryStart(legacySave,legacy);int coins=legacySave.coins;
            var legacyAward=QuestRuntime.Reconcile(legacySave,legacyTable,items,QuestSignal.None());
            Check("legacy no-turn-in still auto completes",legacyAward.Completed.Contains(legacy.Id) && legacySave.coins==coins+7);
            var payable=new QuestDef("payable","交还",1,QuestLedger.StatusAvailable,new List<QuestObjective>(),new List<string>(),new QuestReward(11,new[]{new ItemStack("jinchuangyao",1)},new[]{"paid-once"}),"",npc,npc);
            var multiple=new QuestTable(new List<QuestDef>{legacy,payable,quest});var multiSave=repository.Create(3,"farmer","优先级");QuestLedger.TryStart(multiSave,quest);
            Check("available outranks in-progress",QuestRuntime.OverheadState(multiSave,multiple,npc)==QuestOverheadState.Available);
            QuestLedger.TryStart(multiSave,payable);Check("completable outranks available",QuestRuntime.OverheadState(multiSave,multiple,npc)==QuestOverheadState.Completable);
            coins=multiSave.coins;QuestRuntime.TryTurnIn(multiSave,multiple,items,payable.Id,npc,10);before=Snapshot(multiSave);
            Check("coins items flags award exactly once",multiSave.coins==coins+11 && multiSave.HasFlag("paid-once") && QuestRuntime.TryTurnIn(multiSave,multiple,items,payable.Id,npc,10).Completed.Count==0 && Snapshot(multiSave)==before);
            var bad=new QuestTable(new List<QuestDef>{new QuestDef("bad","bad",1,QuestLedger.StatusAvailable,new List<QuestObjective>(),new List<string>(),new QuestReward(0,Array.Empty<ItemStack>(),Array.Empty<string>()),"","missing",npc)});
            bool rejected=false;try{bad.CrossCheckNpcs(town);}catch(FormatException){rejected=true;}Check("metadata must point at a real world NPC",rejected);
        }
    }
}
