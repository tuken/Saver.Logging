using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Permissions;
using System.Text;
using System.Threading;

namespace Saver.Logging
{
    [HostProtection(Synchronization = true)]
    public class RotateTextWriterTraceListener : TraceListener
    {
        private string _fileNameTemplate = null;

        /// <summary>
        /// ファイル名のテンプレート
        /// </summary>
        private string FileNameTemplate
        {
            get { return _fileNameTemplate; }
        }

        private string _dateFormat = "yyyyMMdd";

        /// <summary>
        /// 日付部分のテンプレート
        /// </summary>
        private string DateFormat
        {
            get { LoadAttribute(); return _dateFormat; }
        }

        private string _versionFormat = "";

        /// <summary>
        /// ファイルバージョン部分のテンプレート
        /// </summary>
        private string VersionFormat
        {
            get { LoadAttribute(); return _versionFormat; }
        }

        private string _datePlaceHolder = "%YYYYMMDD%";

        /// <summary>
        /// ファイル名テンプレートに含まれる日付のプレースホルダ
        /// </summary>
        private string DatePlaceHolder
        {
            get { LoadAttribute(); return _datePlaceHolder; }
        }

        private string _versionPlaceHolder = "%VERSION%";

        /// <summary>
        /// ファイル名テンプレートに含まれるバージョンのプレースフォルダ
        /// </summary>
        private string VersionPlaceHolder
        {
            get { LoadAttribute(); return _versionPlaceHolder; }
        }

        private long _maxSize = 64 * 1024 * 1024;

        /// <summary>
        /// トレースファイルの最大バイト数
        /// </summary>
        private long MaxSize
        {
            get { LoadAttribute(); return _maxSize; }
        }

        private Encoding _encoding = Encoding.GetEncoding("Shift_JIS");

        /// <summary>
        /// 出力ファイルのエンコーディング
        /// </summary>
        private Encoding Encoding
        {
            get { LoadAttribute(); return _encoding; }
        }

        private bool _appendDate = true;

        /// <summary>
        /// 日時をログに付与するかどうか
        /// </summary>
        private bool AppendDate
        {
            get { LoadAttribute(); return _appendDate; }
        }

        private bool _appendThreadId = true;

        /// <summary>
        /// スレッドIDをログに付与するかどうか
        /// </summary>
        private bool AppendThreadId
        {
            get { LoadAttribute(); return _appendThreadId; }
        }

        #region 内部使用フィールド

        /// <summary>
        /// 出力バッファストリーム
        /// </summary>
        private TextWriter _stream = null;

        /// <summary>
        /// 実際に出力されるストリーム
        /// </summary>
        private Stream _baseStream = null;

        /// <summary>
        /// 現在のログ日付
        /// </summary>
        private DateTime _logDate = DateTime.MinValue;

        /// <summary>
        /// バッファサイズ
        /// </summary>
        private int _bufferSize = 4096;

        /// <summary>
        /// ロックオブジェクト
        /// </summary>
        private object _lockObj = new Object();

        /// <summary>
        /// カスタム属性読み込みフラグ
        /// </summary>
        private bool _attributeLoaded = false;

        #endregion

        /// <summary>
        /// スレッドセーフ
        /// </summary>
        public override bool IsThreadSafe
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="fileNameTemplate">ファイル名のテンプレート</param>
        public RotateTextWriterTraceListener(string fileNameTemplate)
        {
            _fileNameTemplate = fileNameTemplate;
        }

        /// <summary>
        /// メッセージを出力します
        /// </summary>
        /// <param name="message"></param>
        public override void Write(string message)
        {
            lock (_lockObj)
            {
                if (EnsureTextWriter())
                {
                    if (NeedIndent) WriteIndent();

                    if (this._appendDate) _stream.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] "));

                    if (this._appendThreadId) _stream.Write("[tid-" + Thread.CurrentContext.ContextID + "] " + message);
                    else _stream.Write(message);

                    _stream.Flush();
                }
            }
        }

        public override void WriteLine(string message)
        {
            Write(message + Environment.NewLine);
        }

        public override void Close()
        {
            lock (_lockObj)
            {
                if (_stream != null) _stream.Close();

                _stream = null;
                _baseStream = null;
            }
        }

        public override void Flush()
        {
            lock (_lockObj)
            {
                if (_stream != null) _stream.Flush(); 
            }
        }

        /// <summary>
        /// 廃棄処理
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Close();

            base.Dispose(disposing);
        }

        /// <summary>
        /// 出力ストリームを準備する
        /// </summary>
        /// <returns></returns>
        private bool EnsureTextWriter()
        {
            if (string.IsNullOrEmpty(FileNameTemplate)) return false;

            DateTime now = DateTime.Now;
            if (_logDate.Date != now.Date) Close(); 
            if (_stream != null && _baseStream.Length > MaxSize) Close();
            if (_stream == null)
            {
                string filepath = NextFileName(now);

                // フルパスを求めると同時にファイル名に不正文字がないことの検証
                string fullpath = Path.GetFullPath(filepath);

                StreamWriter writer = new StreamWriter(fullpath, true, Encoding, _bufferSize);
                _stream = writer;
                _baseStream = writer.BaseStream;
                _logDate = now;
            }

            return true;
        }

        /// <summary>
        /// パスで指定されたディレクトリが存在しなければ作成する。
        /// </summary>
        /// <param name="dirpath">ディレクトリのパス</param>
        /// <returns>作成した場合はtrue</returns>
        private bool CreateDirectoryIfNotExists(string dirpath)
        {
            if (!Directory.Exists(dirpath))
            {
                // 同時に作成してもエラーにならないため例外処理をしない
                Directory.CreateDirectory(dirpath);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 指定されたファイルがログファイルとして使用できるかの判定を行う
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        private bool IsValidLogFile(string filepath)
        {
            if (File.Exists(filepath))
            {
                FileInfo fi = new FileInfo(filepath);
                // 最大サイズより小さければ追記書き込みできるので OK
                if (fi.Length < MaxSize) return true;

                // 最大サイズ以上でもバージョンサポートをしていない場合はOK
                if (!FileNameTemplate.Contains(VersionPlaceHolder)) return true;

                // そうでない場合はNG
                return false;
            }

            return true;
        }

        /// <summary>
        /// 日付に基づくバージョンつきのログファイルのパスを作成する。
        /// </summary>
        /// <param name="logDateTime">ログ日付</param>
        /// <returns></returns>
        private string NextFileName(DateTime logDateTime)
        {
            int version = 0;
            string filepath = ResolveFileName(logDateTime, version);
            string dir = Path.GetDirectoryName(filepath);
            CreateDirectoryIfNotExists(dir);

            while (!IsValidLogFile(filepath))
            {
                ++version;
                filepath = ResolveFileName(logDateTime, version);
            }

            return filepath;
        }

        /// <summary>
        /// ファイル名のテンプレートから日付バージョンを置き換えるヘルパ
        /// </summary>
        /// <param name="logDateTime"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        private string ResolveFileName(DateTime logDateTime, int version)
        {
            string t = FileNameTemplate;
            if (t.Contains(DatePlaceHolder)) t = t.Replace(DatePlaceHolder, logDateTime.ToString(DateFormat));
            if (t.Contains(VersionPlaceHolder)) t = t.Replace(VersionPlaceHolder, version.ToString(VersionFormat));
            return t;
        }

        #region カスタム属性用

        /// <summary>
        /// サポートされているカスタム属性
        /// MaxSize : ログファイルの最大サイズ
        /// Encoding: 文字コード
        /// DateFormat:ログファイル名の日付部分のフォーマット文字列
        /// VersionFormat: ログファイルのバージョン部分のフォーマット文字列
        /// DatePlaceHolder: ファイル名テンプレートの日付部分のプレースホルダ文字列
        /// VersionPlaceHolder: ファイル名テンプレートのバージョブ部分のプレースホルダ文字列
        /// </summary>
        /// <returns></returns>
        protected override string[] GetSupportedAttributes()
        {
            return new string[] { "MaxSize", "Encoding", "appendDate", "appendThreadId", "DateFormat", "VersionFormat", "DatePlaceHolder", "VersionPlaceHolder" };
        }

        /// <summary>
        /// カスタム属性
        /// </summary>
        private void LoadAttribute()
        {
            if (!_attributeLoaded)
            {
                // 最大バイト数
                if (Attributes.ContainsKey("MaxSize")) _maxSize = long.Parse(Attributes["MaxSize"]);
                // エンコーディング
                if (Attributes.ContainsKey("Encoding")) _encoding = Encoding.GetEncoding(Attributes["Encoding"]);
                // 日時付与
                if (Attributes.ContainsKey("AppendDate")) _appendDate = bool.Parse(Attributes["AppendDate"]);
                // スレッドID付与
                if (Attributes.ContainsKey("AppendThreadId")) _appendThreadId = bool.Parse(Attributes["AppendThreadId"]);
                // 日付のフォーマット
                if (Attributes.ContainsKey("DateFormat")) _dateFormat = Attributes["DateFormat"];
                // バージョンのフォーマット
                if (Attributes.ContainsKey("VersionFormat")) _versionFormat = Attributes["VersionFormat"];
                // 日付のプレースホルダ
                if (Attributes.ContainsKey("DatePlaceHolder")) _datePlaceHolder = Attributes["DatePlaceHolder"];
                // バージョンのプレースホルダ
                if (Attributes.ContainsKey("VersionPlaceHolder")) _versionPlaceHolder = Attributes["VersionPlaceHolder"];

                _attributeLoaded = true;
            }
        }

        #endregion
    }
}
