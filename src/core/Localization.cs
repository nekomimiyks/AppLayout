using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace AppLayout {
 public sealed class LanguageChoice {
  public readonly string Code, NativeName;
  public LanguageChoice(string code,string name){Code=code;NativeName=name;}
  public override string ToString(){return Code=="auto"?L.T("自動（Windowsの表示言語）"):NativeName;}
 }
 // 日本語の原文をキーにし、翻訳をexe内へ埋め込む。配置名・タイトル条件等の利用者データは対象外。
 public static class L {
  [DataContract] sealed class Translation {
   [DataMember] public string Key {get;set;}
   [DataMember] public string Value {get;set;}
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern bool GetUserPreferredUILanguages(uint flags,out uint count,StringBuilder buffer,ref uint size);
  static readonly string initialWindowsCulture=CultureInfo.CurrentUICulture.Name;
  static readonly Dictionary<string,Dictionary<string,string>> catalogs=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
  static Dictionary<string,string> current=new Dictionary<string,string>();
  static string selection="auto",code="ja";
  sealed class TextKey {public string Key;}
  static readonly ConditionalWeakTable<Control,TextKey> textKeys=new ConditionalWeakTable<Control,TextKey>();
  public static readonly LanguageChoice[] Languages={
   new LanguageChoice("auto",""),new LanguageChoice("ja","日本語"),new LanguageChoice("en","English"),
   new LanguageChoice("zh-Hans","简体中文"),new LanguageChoice("zh-Hant","繁體中文"),new LanguageChoice("ko","한국어"),
   new LanguageChoice("es","Español"),new LanguageChoice("fr","Français"),new LanguageChoice("de","Deutsch"),new LanguageChoice("pt","Português (Brasil)")
  };
  public static event EventHandler Changed;
  public static string Code {get{return code;}}
  public static string Selection {get{return selection;}}
  public static string NormalizeSelection(string value) {
   foreach(var language in Languages)if(string.Equals(language.Code,value,StringComparison.OrdinalIgnoreCase))return language.Code;
   return "auto";
  }
  public static string Resolve(string culture) {
   string name=(culture??"").Replace('_','-').ToLowerInvariant();
   if(name=="zh" || name.StartsWith("zh-"))
    return name.Contains("hant")||name=="zh-tw"||name=="zh-hk"||name=="zh-mo"?"zh-Hant":"zh-Hans";
   string primary=name.Split('-')[0];
   foreach(var language in Languages)if(language.Code==primary && primary!="auto")return primary;
   return "en";
  }
  static string WindowsLanguage() {
   try {
    uint count,size=0;GetUserPreferredUILanguages(8,out count,null,ref size);
    if(size>0 && size<32768) {
     var buffer=new StringBuilder((int)size);
     if(GetUserPreferredUILanguages(8,out count,buffer,ref size) && buffer.Length>0)return buffer.ToString().Split('\0')[0];
    }
   }catch(EntryPointNotFoundException){}catch(DllNotFoundException){}
   return initialWindowsCulture;
  }
  public static void SetLanguage(string value) {
   string nextSelection=NormalizeSelection(value);
   string nextCode=nextSelection=="auto"?Resolve(WindowsLanguage()):nextSelection;
   var nextCatalog=Load(nextCode); // 読み込みに失敗した場合、表示中の言語を保つ。
   bool changed=nextCode!=code || nextSelection!=selection;
   selection=nextSelection;code=nextCode;current=nextCatalog;
   if(changed){var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);}
  }
  static Dictionary<string,string> Load(string language) {
   Dictionary<string,string> result;
   lock(catalogs) {
    if(catalogs.TryGetValue(language,out result))return result;
    result=new Dictionary<string,string>(StringComparer.Ordinal);
    if(language!="ja")using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AppLayout.Locales."+language+".json")) {
     if(stream==null)throw new InvalidDataException("Missing language resource: "+language);
     var items=(Translation[])new DataContractJsonSerializer(typeof(Translation[])).ReadObject(stream);
     foreach(var item in items)if(!string.IsNullOrEmpty(item.Key) && !string.IsNullOrEmpty(item.Value))result.Add(item.Key,item.Value);
    }
    catalogs.Add(language,result);return result;
   }
  }
  public static string T(string source) {
   if(source==null)return "";
   string translated;return current.TryGetValue(source,out translated)?translated:source;
  }
  public static string F(string source,params object[] args){return string.Format(CultureInfo.CurrentCulture,T(source),args);}
  public static string Rule(string value) {
   foreach(string prefix in new[]{"タイトル一致: ","タイトルを含む: ","正規表現: "})
    if(value!=null && value.StartsWith(prefix,StringComparison.Ordinal))return T(prefix)+value.Substring(prefix.Length);
   return T(value);
  }
  static string Source(string text) {
   if(string.IsNullOrEmpty(text))return null;
   // 起動時の静的日本語コントロールも、英語カタログを原文の一覧として検出する。
   if(Load("en").ContainsKey(text))return text;
   string found=null;
   foreach(var pair in current)if(pair.Value==text){if(found!=null)return null;found=pair.Key;}
   return found;
  }
  public static void BindText(Control control,string source) {
   textKeys.GetValue(control,delegate(Control ignored){return new TextKey();}).Key=source;control.Text=T(source);
  }
  public static void Attach(Form form,Action refresh) {
   var apply=new List<Action>();
   Collect(form,apply);
   EventHandler changed=delegate {
    if(form.IsDisposed||form.Disposing)return;
    Action update=delegate {if(form.IsDisposed)return;form.SuspendLayout();try{foreach(var action in apply)action();if(refresh!=null)refresh();}finally{form.ResumeLayout(true);}};
    if(form.IsHandleCreated && form.InvokeRequired){try{form.BeginInvoke(update);}catch(InvalidOperationException){}}else update();
   };
   Changed+=changed;form.Disposed+=delegate {Changed-=changed;};
   changed(null,EventArgs.Empty);
  }
  static void Collect(Control control,List<Action> apply) {
   if(!(control is TextBoxBase) && !(control is UpDownBase) && !(control is ComboBox)) {
    TextKey key;string source=textKeys.TryGetValue(control,out key)?key.Key:Source(control.Text);
    if(source!=null)apply.Add(delegate{control.Text=T(source);});
   }
   var grid=control as DataGridView;
   if(grid!=null)foreach(DataGridViewColumn column in grid.Columns) {
    var target=column;string source=Source(column.HeaderText);if(source!=null)apply.Add(delegate{target.HeaderText=T(source);});
   }
   var list=control as ListView;
   if(list!=null)foreach(ColumnHeader column in list.Columns) {
    var target=column;string source=Source(column.Text);if(source!=null)apply.Add(delegate{target.Text=T(source);});
   }
   string accessible=Source(control.AccessibleName);if(accessible!=null)apply.Add(delegate{control.AccessibleName=T(accessible);});
   foreach(Control child in control.Controls)Collect(child,apply);
  }
 }
}
