using System.Drawing;
using System.Reflection;
var app=Assembly.Load("FishingAutomation");
const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
var policy=app.GetType("DungeonVisionBot.AbyssRewardVisualPolicy",true)!;
var detect=policy.GetMethod("TryDetect",flags)!;
void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
bool Reward(Bitmap b){object?[] p={b,null,null};bool ok=(bool)detect.Invoke(null,p)!;Console.WriteLine(p[2]);return ok;}
foreach(var name in new[]{"reward_2335","reward_0020","clear_2253","combat_2006","combat_2258"})
{
 using var b=new Bitmap(Path.Combine(AppContext.BaseDirectory,"fixtures",name+".png"));
 Check(Reward(b)==name.StartsWith("reward"),"actual incident "+name);
 if(name.StartsWith("reward"))
 {
  using var changed=(Bitmap)b.Clone();using(var g=Graphics.FromImage(changed))g.FillRectangle(Brushes.Black,350,105,100,100);
  Check(!Reward(changed),"missing chest rejected");
  using(var g=Graphics.FromImage(changed)){g.FillRectangle(Brushes.Goldenrod,350,105,100,100);g.FillRectangle(Brushes.Lime,180,922,440,56);}
  Check(!Reward(changed),"single full-width button rejected");
 }
}
if(args.Contains("--ocr"))
{
 var ocrType=app.GetType("DungeonVisionBot.OcrRecognizer",true)!;
 var ocr=Activator.CreateInstance(ocrType)!;
 var find=ocrType.GetMethod("FindCompactLabelAsync",flags)!;
 foreach(var name in new[]{"clear_2253","combat_2006","combat_2258","reward_0020"})
 {
  using var b=new Bitmap(Path.Combine(AppContext.BaseDirectory,"fixtures",name+".png"));
  var task=(Task)find.Invoke(ocr,new object[]{b,new Rectangle(245,890,310,105),"화면을 터치해 주세요",CancellationToken.None})!;
  await task;var result=task.GetType().GetProperty("Result")!.GetValue(task)!;
  bool found=(bool)result.GetType().GetProperty("Found")!.GetValue(result)!;
  Check(found==name.StartsWith("clear"),"production Korean OCR "+name);
 }
}
