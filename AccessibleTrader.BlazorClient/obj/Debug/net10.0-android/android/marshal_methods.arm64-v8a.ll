; ModuleID = 'marshal_methods.arm64-v8a.ll'
source_filename = "marshal_methods.arm64-v8a.ll"
target datalayout = "e-m:e-i8:8:32-i16:16:32-i64:64-i128:128-n32:64-S128"
target triple = "aarch64-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [436 x ptr] zeroinitializer, align 8

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [1308 x i64] [
	i64 u0x001e58127c546039, ; 0: lib_System.Globalization.dll.so => 42
	i64 u0x0024d0f62dee05bd, ; 1: Xamarin.KotlinX.Coroutines.Core.dll => 337
	i64 u0x003f21f150fd9b84, ; 2: lib-cs-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 380
	i64 u0x0071cf2d27b7d61e, ; 3: lib_Xamarin.AndroidX.SwipeRefreshLayout.dll.so => 316
	i64 u0x01109b0e4d99e61f, ; 4: System.ComponentModel.Annotations.dll => 13
	i64 u0x02123411c4e01926, ; 5: lib_Xamarin.AndroidX.Navigation.Runtime.dll.so => 304
	i64 u0x022f31be406de945, ; 6: Microsoft.Extensions.Options.ConfigurationExtensions => 222
	i64 u0x0284512fad379f7e, ; 7: System.Runtime.Handles => 107
	i64 u0x02a4c5a44384f885, ; 8: Microsoft.Extensions.Caching.Memory => 200
	i64 u0x02abedc11addc1ed, ; 9: lib_Mono.Android.Runtime.dll.so => 174
	i64 u0x02f55bf70672f5c8, ; 10: lib_System.IO.FileSystem.DriveInfo.dll.so => 48
	i64 u0x031c967c64bb4b18, ; 11: AccessibleTrader.Plugins.Bitstamp => 430
	i64 u0x032267b2a94db371, ; 12: lib_Xamarin.AndroidX.AppCompat.dll.so => 258
	i64 u0x03621c804933a890, ; 13: System.Buffers => 7
	i64 u0x0363ac97a4cb84e6, ; 14: SQLitePCLRaw.provider.e_sqlite3.dll => 246
	i64 u0x0399610510a38a38, ; 15: lib_System.Private.DataContractSerialization.dll.so => 88
	i64 u0x043032f1d071fae0, ; 16: ru/Microsoft.Maui.Controls.resources => 417
	i64 u0x044440a55165631e, ; 17: lib-cs-Microsoft.Maui.Controls.resources.dll.so => 395
	i64 u0x046eb1581a80c6b0, ; 18: vi/Microsoft.Maui.Controls.resources => 423
	i64 u0x0470607fd33c32db, ; 19: Microsoft.IdentityModel.Abstractions.dll => 225
	i64 u0x047408741db2431a, ; 20: Xamarin.AndroidX.DynamicAnimation => 278
	i64 u0x0517ef04e06e9f76, ; 21: System.Net.Primitives => 72
	i64 u0x0565d18c6da3de38, ; 22: Xamarin.AndroidX.RecyclerView => 308
	i64 u0x057bf9fa9fb09f7c, ; 23: Microsoft.Data.Sqlite.dll => 194
	i64 u0x0581db89237110e9, ; 24: lib_System.Collections.dll.so => 12
	i64 u0x05989cb940b225a9, ; 25: Microsoft.Maui.dll => 232
	i64 u0x05a1c25e78e22d87, ; 26: lib_System.Runtime.CompilerServices.Unsafe.dll.so => 104
	i64 u0x05ef98b6a1db882c, ; 27: lib_Microsoft.Data.Sqlite.dll.so => 194
	i64 u0x06076b5d2b581f08, ; 28: zh-HK/Microsoft.Maui.Controls.resources => 424
	i64 u0x06388ffe9f6c161a, ; 29: System.Xml.Linq.dll => 159
	i64 u0x06600c4c124cb358, ; 30: System.Configuration.dll => 19
	i64 u0x067f95c5ddab55b3, ; 31: lib_Xamarin.AndroidX.Fragment.Ktx.dll.so => 283
	i64 u0x0680a433c781bb3d, ; 32: Xamarin.AndroidX.Collection.Jvm => 265
	i64 u0x0690533f9fc14683, ; 33: lib_Microsoft.AspNetCore.Components.dll.so => 184
	i64 u0x069fff96ec92a91d, ; 34: System.Xml.XPath.dll => 164
	i64 u0x070b0847e18dab68, ; 35: Xamarin.AndroidX.Emoji2.ViewsHelper.dll => 280
	i64 u0x0739448d84d3b016, ; 36: lib_Xamarin.AndroidX.VectorDrawable.dll.so => 320
	i64 u0x07469f2eecce9e85, ; 37: mscorlib.dll => 170
	i64 u0x07c57877c7ba78ad, ; 38: ru/Microsoft.Maui.Controls.resources.dll => 417
	i64 u0x07dcdc7460a0c5e4, ; 39: System.Collections.NonGeneric => 10
	i64 u0x08122e52765333c8, ; 40: lib_Microsoft.Extensions.Logging.Debug.dll.so => 220
	i64 u0x088610fc2509f69e, ; 41: lib_Xamarin.AndroidX.VectorDrawable.Animated.dll.so => 321
	i64 u0x08a7c865576bbde7, ; 42: System.Reflection.Primitives => 98
	i64 u0x08c9d051a4a817e5, ; 43: Xamarin.AndroidX.CustomView.PoolingContainer.dll => 276
	i64 u0x08f3c9788ee2153c, ; 44: Xamarin.AndroidX.DrawerLayout => 277
	i64 u0x09138715c92dba90, ; 45: lib_System.ComponentModel.Annotations.dll.so => 13
	i64 u0x0919c28b89381a0b, ; 46: lib_Microsoft.Extensions.Options.dll.so => 221
	i64 u0x092266563089ae3e, ; 47: lib_System.Collections.NonGeneric.dll.so => 10
	i64 u0x092a18ee63d56e73, ; 48: de/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 368
	i64 u0x09d144a7e214d457, ; 49: System.Security.Cryptography => 129
	i64 u0x09e2b9f743db21a8, ; 50: lib_System.Reflection.Metadata.dll.so => 97
	i64 u0x0a805f95d98f597b, ; 51: lib_Microsoft.Extensions.Caching.Abstractions.dll.so => 199
	i64 u0x0abb3e2b271edc45, ; 52: System.Threading.Channels.dll => 143
	i64 u0x0b06b1feab070143, ; 53: System.Formats.Tar => 39
	i64 u0x0b3b632c3bbee20c, ; 54: sk/Microsoft.Maui.Controls.resources => 418
	i64 u0x0b6aff547b84fbe9, ; 55: Xamarin.KotlinX.Serialization.Core.Jvm => 340
	i64 u0x0be2e1f8ce4064ed, ; 56: Xamarin.AndroidX.ViewPager => 323
	i64 u0x0c3ca6cc978e2aae, ; 57: pt-BR/Microsoft.Maui.Controls.resources => 414
	i64 u0x0c59ad9fbbd43abe, ; 58: Mono.Android => 175
	i64 u0x0c65741e86371ee3, ; 59: lib_Xamarin.Android.Glide.GifDecoder.dll.so => 252
	i64 u0x0c74af560004e816, ; 60: Microsoft.Win32.Registry.dll => 5
	i64 u0x0c7790f60165fc06, ; 61: lib_Microsoft.Maui.Essentials.dll.so => 233
	i64 u0x0c83c82812e96127, ; 62: lib_System.Net.Mail.dll.so => 68
	i64 u0x0cce4bce83380b7f, ; 63: Xamarin.AndroidX.Security.SecurityCrypto => 313
	i64 u0x0cf6a95dadccbb9c, ; 64: zh-Hant/Microsoft.CodeAnalysis.resources.dll => 353
	i64 u0x0d13cd7cce4284e4, ; 65: System.Security.SecureString => 132
	i64 u0x0d63f4f73521c24f, ; 66: lib_Xamarin.AndroidX.SavedState.SavedState.Ktx.dll.so => 312
	i64 u0x0d86063f20335c36, ; 67: lib_Skender.Stock.Indicators.dll.so => 238
	i64 u0x0e04e702012f8463, ; 68: Xamarin.AndroidX.Emoji2 => 279
	i64 u0x0e14e73a54dda68e, ; 69: lib_System.Net.NameResolution.dll.so => 69
	i64 u0x0e7acf675d09f75a, ; 70: it/Microsoft.CodeAnalysis.resources => 345
	i64 u0x0ec01b05613190b9, ; 71: SkiaSharp.Views.Android.dll => 240
	i64 u0x0ec47e16319c99d9, ; 72: lib-de-Microsoft.CodeAnalysis.resources.dll.so => 342
	i64 u0x0f37dd7a62ae99af, ; 73: lib_Xamarin.AndroidX.Collection.Ktx.dll.so => 266
	i64 u0x0f5e7abaa7cf470a, ; 74: System.Net.HttpListener => 67
	i64 u0x1001f97bbe242e64, ; 75: System.IO.UnmanagedMemoryStream => 57
	i64 u0x102a31b45304b1da, ; 76: Xamarin.AndroidX.CustomView => 275
	i64 u0x105b053cfbaba1f0, ; 77: lib_Microsoft.CodeAnalysis.dll.so => 190
	i64 u0x1065c4cb554c3d75, ; 78: System.IO.IsolatedStorage.dll => 52
	i64 u0x10a579e648829775, ; 79: Microsoft.CodeAnalysis => 190
	i64 u0x10e259b85a28bd6c, ; 80: de/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 368
	i64 u0x10f6cfcbcf801616, ; 81: System.IO.Compression.Brotli => 43
	i64 u0x114443cdcf2091f1, ; 82: System.Security.Cryptography.Primitives => 127
	i64 u0x114df3ff11650a65, ; 83: ru/Microsoft.CodeAnalysis.CSharp.resources => 363
	i64 u0x114f9c84eae55607, ; 84: zh-Hant/Microsoft.CodeAnalysis.Scripting.resources.dll => 392
	i64 u0x11a603952763e1d4, ; 85: System.Net.Mail => 68
	i64 u0x11a70d0e1009fb11, ; 86: System.Net.WebSockets.dll => 83
	i64 u0x11f26371eee0d3c1, ; 87: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll.so => 293
	i64 u0x1208da3842d90ff3, ; 88: lib-ko-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 360
	i64 u0x12128b3f59302d47, ; 89: lib_System.Xml.Serialization.dll.so => 161
	i64 u0x123639456fb056da, ; 90: System.Reflection.Emit.Lightweight.dll => 94
	i64 u0x12521e9764603eaa, ; 91: lib_System.Resources.Reader.dll.so => 101
	i64 u0x125b7f94acb989db, ; 92: Xamarin.AndroidX.RecyclerView.dll => 308
	i64 u0x12771c3b6490ec33, ; 93: ja/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 372
	i64 u0x12d3b63863d4ab0b, ; 94: lib_System.Threading.Overlapped.dll.so => 144
	i64 u0x131463e9417f52d4, ; 95: de/Microsoft.CodeAnalysis.CSharp.resources => 355
	i64 u0x134eab1061c395ee, ; 96: System.Transactions => 154
	i64 u0x138567fa954faa55, ; 97: Xamarin.AndroidX.Browser => 262
	i64 u0x1393617ead22674a, ; 98: zh-Hant/Microsoft.CodeAnalysis.resources => 353
	i64 u0x139989e2fa3384e8, ; 99: ru/Microsoft.CodeAnalysis.Scripting.resources => 389
	i64 u0x13a01de0cbc3f06c, ; 100: lib-fr-Microsoft.Maui.Controls.resources.dll.so => 401
	i64 u0x13beedefb0e28a45, ; 101: lib_System.Xml.XmlDocument.dll.so => 165
	i64 u0x13f1e5e209e91af4, ; 102: lib_Java.Interop.dll.so => 172
	i64 u0x13f1e880c25d96d1, ; 103: he/Microsoft.Maui.Controls.resources => 402
	i64 u0x143d8ea60a6a4011, ; 104: Microsoft.Extensions.DependencyInjection.Abstractions => 207
	i64 u0x1446c7a06695f3ea, ; 105: ko/Microsoft.CodeAnalysis.CSharp.resources.dll => 360
	i64 u0x1497051b917530bd, ; 106: lib_System.Net.WebSockets.dll.so => 83
	i64 u0x14d612a531c79c05, ; 107: Xamarin.JSpecify.dll => 334
	i64 u0x14e68447938213b7, ; 108: Xamarin.AndroidX.Collection.Ktx.dll => 266
	i64 u0x1506378c0000a92a, ; 109: lib-tr-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 364
	i64 u0x152a448bd1e745a7, ; 110: Microsoft.Win32.Primitives => 4
	i64 u0x154f63b58010b003, ; 111: lib-zh-Hant-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 392
	i64 u0x1557de0138c445f4, ; 112: lib_Microsoft.Win32.Registry.dll.so => 5
	i64 u0x15bdc156ed462f2f, ; 113: lib_System.IO.FileSystem.dll.so => 51
	i64 u0x15e300c2c1668655, ; 114: System.Resources.Writer.dll => 103
	i64 u0x16054fdcb6b3098b, ; 115: Microsoft.Extensions.DependencyModel.dll => 208
	i64 u0x16bf2a22df043a09, ; 116: System.IO.Pipes.dll => 56
	i64 u0x16ea2b318ad2d830, ; 117: System.Security.Cryptography.Algorithms => 122
	i64 u0x16eeae54c7ebcc08, ; 118: System.Reflection.dll => 100
	i64 u0x17125c9a85b4929f, ; 119: lib_netstandard.dll.so => 171
	i64 u0x1716866f7416792e, ; 120: lib_System.Security.AccessControl.dll.so => 120
	i64 u0x174f71c46216e44a, ; 121: Xamarin.KotlinX.Coroutines.Core => 337
	i64 u0x1752c12f1e1fc00c, ; 122: System.Core => 21
	i64 u0x17b56e25558a5d36, ; 123: lib-hu-Microsoft.Maui.Controls.resources.dll.so => 405
	i64 u0x17b9399fe6696d5c, ; 124: lib-tr-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 390
	i64 u0x17f9358913beb16a, ; 125: System.Text.Encodings.Web => 139
	i64 u0x1805f780a2be57b5, ; 126: Polly.Core.dll => 237
	i64 u0x1809fb23f29ba44a, ; 127: lib_System.Reflection.TypeExtensions.dll.so => 99
	i64 u0x18402a709e357f3b, ; 128: lib_Xamarin.KotlinX.Serialization.Core.Jvm.dll.so => 340
	i64 u0x18950fae1c2bc98e, ; 129: lib-cs-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 354
	i64 u0x18a9befae51bb361, ; 130: System.Net.WebClient => 79
	i64 u0x18f0ce884e87d89a, ; 131: nb/Microsoft.Maui.Controls.resources.dll => 411
	i64 u0x192712eaa333180f, ; 132: lib-zh-Hant-Microsoft.CodeAnalysis.resources.dll.so => 353
	i64 u0x19777fba3c41b398, ; 133: Xamarin.AndroidX.Startup.StartupRuntime.dll => 315
	i64 u0x19a4c090f14ebb66, ; 134: System.Security.Claims => 121
	i64 u0x1a761daba47c6ad5, ; 135: ja/Microsoft.CodeAnalysis.resources.dll => 346
	i64 u0x1a91866a319e9259, ; 136: lib_System.Collections.Concurrent.dll.so => 8
	i64 u0x1a9e139e4762aaf8, ; 137: es/Microsoft.CodeAnalysis.CSharp.resources.dll => 356
	i64 u0x1aac34d1917ba5d3, ; 138: lib_System.dll.so => 168
	i64 u0x1aad60783ffa3e5b, ; 139: lib-th-Microsoft.Maui.Controls.resources.dll.so => 420
	i64 u0x1aea8f1c3b282172, ; 140: lib_System.Net.Ping.dll.so => 71
	i64 u0x1b4b7a1d0d265fa2, ; 141: Xamarin.Android.Glide.DiskLruCache => 251
	i64 u0x1b8700ce6e547c0b, ; 142: lib_Microsoft.AspNetCore.Components.Forms.dll.so => 185
	i64 u0x1bbdb16cfa73e785, ; 143: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android => 294
	i64 u0x1bc766e07b2b4241, ; 144: Xamarin.AndroidX.ResourceInspection.Annotation.dll => 309
	i64 u0x1c074bdeeae2e1c9, ; 145: lib-pl-Microsoft.CodeAnalysis.resources.dll.so => 348
	i64 u0x1c292b1598348d77, ; 146: Microsoft.Extensions.Diagnostics.dll => 209
	i64 u0x1c5217a9e4973753, ; 147: lib_Microsoft.Extensions.FileProviders.Physical.dll.so => 214
	i64 u0x1c753b5ff15bce1b, ; 148: Mono.Android.Runtime.dll => 174
	i64 u0x1cd47467799d8250, ; 149: System.Threading.Tasks.dll => 148
	i64 u0x1d23eafdc6dc346c, ; 150: System.Globalization.Calendars.dll => 40
	i64 u0x1d88944b182faec5, ; 151: fr/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 370
	i64 u0x1da4110562816681, ; 152: Xamarin.AndroidX.Security.SecurityCrypto.dll => 313
	i64 u0x1db6820994506bf5, ; 153: System.IO.FileSystem.AccessControl.dll => 47
	i64 u0x1dbb0c2c6a999acb, ; 154: System.Diagnostics.StackTrace => 30
	i64 u0x1e3d87657e9659bc, ; 155: Xamarin.AndroidX.Navigation.UI => 306
	i64 u0x1e71143913d56c10, ; 156: lib-ko-Microsoft.Maui.Controls.resources.dll.so => 409
	i64 u0x1e7c31185e2fb266, ; 157: lib_System.Threading.Tasks.Parallel.dll.so => 147
	i64 u0x1ed8fcce5e9b50a0, ; 158: Microsoft.Extensions.Options.dll => 221
	i64 u0x1f055d15d807e1b2, ; 159: System.Xml.XmlSerializer => 166
	i64 u0x1f1ed22c1085f044, ; 160: lib_System.Diagnostics.FileVersionInfo.dll.so => 28
	i64 u0x1f59668779696d49, ; 161: lib-pl-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 374
	i64 u0x1f61df9c5b94d2c1, ; 162: lib_System.Numerics.dll.so => 86
	i64 u0x1f750bb5421397de, ; 163: lib_Xamarin.AndroidX.Tracing.Tracing.dll.so => 317
	i64 u0x1fc7dd520ea7d6b9, ; 164: lib-pl-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 387
	i64 u0x20237ea48006d7a8, ; 165: lib_System.Net.WebClient.dll.so => 79
	i64 u0x209375905fcc1bad, ; 166: lib_System.IO.Compression.Brotli.dll.so => 43
	i64 u0x20fab3cf2dfbc8df, ; 167: lib_System.Diagnostics.Process.dll.so => 29
	i64 u0x210c5bb16b7260a2, ; 168: lib_Microsoft.Extensions.Validation.dll.so => 224
	i64 u0x2110167c128cba15, ; 169: System.Globalization => 42
	i64 u0x21419508838f7547, ; 170: System.Runtime.CompilerServices.VisualC => 105
	i64 u0x2174319c0d835bc9, ; 171: System.Runtime => 119
	i64 u0x217470d3ba7d3c8c, ; 172: lib-de-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 368
	i64 u0x2198e5bc8b7153fa, ; 173: Xamarin.AndroidX.Annotation.Experimental.dll => 256
	i64 u0x219ea1b751a4dee4, ; 174: lib_System.IO.Compression.ZipFile.dll.so => 45
	i64 u0x21cc7e445dcd5469, ; 175: System.Reflection.Emit.ILGeneration => 93
	i64 u0x220fd4f2e7c48170, ; 176: th/Microsoft.Maui.Controls.resources => 420
	i64 u0x224538d85ed15a82, ; 177: System.IO.Pipes => 56
	i64 u0x22908438c6bed1af, ; 178: lib_System.Threading.Timer.dll.so => 151
	i64 u0x237be844f1f812c7, ; 179: System.Threading.Thread.dll => 149
	i64 u0x23807c59646ec4f3, ; 180: lib_Microsoft.EntityFrameworkCore.dll.so => 195
	i64 u0x23852b3bdc9f7096, ; 181: System.Resources.ResourceManager => 102
	i64 u0x23986dd7e5d4fc01, ; 182: System.IO.FileSystem.Primitives.dll => 49
	i64 u0x2407aef2bbe8fadf, ; 183: System.Console => 20
	i64 u0x240abe014b27e7d3, ; 184: Xamarin.AndroidX.Core.dll => 271
	i64 u0x245ebc45bf698558, ; 185: ru/Microsoft.CodeAnalysis.resources.dll => 350
	i64 u0x247619fe4413f8bf, ; 186: System.Runtime.Serialization.Primitives.dll => 116
	i64 u0x24de8d301281575e, ; 187: Xamarin.Android.Glide => 249
	i64 u0x252073cc3caa62c2, ; 188: fr/Microsoft.Maui.Controls.resources.dll => 401
	i64 u0x256b8d41255f01b1, ; 189: Xamarin.Google.Crypto.Tink.Android => 330
	i64 u0x259e27cab50e0c41, ; 190: lib-es-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 369
	i64 u0x25a0a7eff76ea08e, ; 191: SQLitePCLRaw.batteries_v2.dll => 243
	i64 u0x2604a92c4e777626, ; 192: it/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 371
	i64 u0x2662c629b96b0b30, ; 193: lib_Xamarin.Kotlin.StdLib.dll.so => 335
	i64 u0x268c1439f13bcc29, ; 194: lib_Microsoft.Extensions.Primitives.dll.so => 223
	i64 u0x26a670e154a9c54b, ; 195: System.Reflection.Extensions.dll => 96
	i64 u0x26d077d9678fe34f, ; 196: System.IO.dll => 58
	i64 u0x270a44600c921861, ; 197: System.IdentityModel.Tokens.Jwt => 247
	i64 u0x272377f9edc266a2, ; 198: tr/Microsoft.CodeAnalysis.resources => 351
	i64 u0x273f3515de5faf0d, ; 199: id/Microsoft.Maui.Controls.resources.dll => 406
	i64 u0x2742545f9094896d, ; 200: hr/Microsoft.Maui.Controls.resources => 404
	i64 u0x27458c1163a5ce22, ; 201: Microsoft.Extensions.Validation => 224
	i64 u0x274d85d83ad40513, ; 202: lib_Xamarin.AndroidX.Window.WindowCore.dll.so => 326
	i64 u0x2759af78ab94d39b, ; 203: System.Net.WebSockets => 83
	i64 u0x27b2b16f3e9de038, ; 204: Xamarin.Google.Crypto.Tink.Android.dll => 330
	i64 u0x27b410442fad6cf1, ; 205: Java.Interop.dll => 172
	i64 u0x27b97e0d52c3034a, ; 206: System.Diagnostics.Debug => 26
	i64 u0x2801845a2c71fbfb, ; 207: System.Net.Primitives.dll => 72
	i64 u0x286835e259162700, ; 208: lib_Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll.so => 307
	i64 u0x28e52865585a1ebe, ; 209: Microsoft.Extensions.Diagnostics.Abstractions.dll => 210
	i64 u0x2927d345f3daec35, ; 210: SkiaSharp.dll => 239
	i64 u0x2949f3617a02c6b2, ; 211: Xamarin.AndroidX.ExifInterface => 281
	i64 u0x29aeab763a527e52, ; 212: lib_Xamarin.AndroidX.Navigation.Common.Android.dll.so => 302
	i64 u0x2a128783efe70ba0, ; 213: uk/Microsoft.Maui.Controls.resources.dll => 422
	i64 u0x2a159655d05d918f, ; 214: es/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 369
	i64 u0x2a3b095612184159, ; 215: lib_System.Net.NetworkInformation.dll.so => 70
	i64 u0x2a6507a5ffabdf28, ; 216: System.Diagnostics.TraceSource.dll => 33
	i64 u0x2ac22cb244daa1d5, ; 217: zh-Hans/Microsoft.CodeAnalysis.Scripting.resources.dll => 391
	i64 u0x2ad156c8e1354139, ; 218: fi/Microsoft.Maui.Controls.resources => 400
	i64 u0x2ad5d6b13b7a3e04, ; 219: System.ComponentModel.DataAnnotations.dll => 14
	i64 u0x2af298f63581d886, ; 220: System.Text.RegularExpressions.dll => 141
	i64 u0x2af615542f04da50, ; 221: System.IdentityModel.Tokens.Jwt.dll => 247
	i64 u0x2afc1c4f898552ee, ; 222: lib_System.Formats.Asn1.dll.so => 38
	i64 u0x2b148910ed40fbf9, ; 223: zh-Hant/Microsoft.Maui.Controls.resources.dll => 426
	i64 u0x2b4d4904cebfa4e9, ; 224: Microsoft.Extensions.FileSystemGlobbing => 215
	i64 u0x2b6989d78cba9a15, ; 225: Xamarin.AndroidX.Concurrent.Futures.dll => 267
	i64 u0x2b8afb093774d1b8, ; 226: CryptoExchange.Net.dll => 180
	i64 u0x2c8bd14bb93a7d82, ; 227: lib-pl-Microsoft.Maui.Controls.resources.dll.so => 413
	i64 u0x2cbae5420bee4a22, ; 228: AccessibleTrader.Plugins.Bitstamp.dll => 430
	i64 u0x2cbd9262ca785540, ; 229: lib_System.Text.Encoding.CodePages.dll.so => 136
	i64 u0x2cc9e1fed6257257, ; 230: lib_System.Reflection.Emit.Lightweight.dll.so => 94
	i64 u0x2cd723e9fe623c7c, ; 231: lib_System.Private.Xml.Linq.dll.so => 90
	i64 u0x2d169d318a968379, ; 232: System.Threading.dll => 152
	i64 u0x2d20145f27cfc1d2, ; 233: Xamarin.AndroidX.Window.WindowCore.Jvm.dll => 327
	i64 u0x2d42a09cd7c7d276, ; 234: lib-ja-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 385
	i64 u0x2d47774b7d993f59, ; 235: sv/Microsoft.Maui.Controls.resources.dll => 419
	i64 u0x2d5ffcae1ad0aaca, ; 236: System.Data.dll => 24
	i64 u0x2db915caf23548d2, ; 237: System.Text.Json.dll => 140
	i64 u0x2dcaa0bb15a4117a, ; 238: System.IO.UnmanagedMemoryStream.dll => 57
	i64 u0x2dd30aab497bcb81, ; 239: fr/Microsoft.CodeAnalysis.Scripting.resources => 383
	i64 u0x2df6accf4889fda3, ; 240: lib-pt-BR-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 375
	i64 u0x2e005eafd348fcc4, ; 241: ja/Microsoft.CodeAnalysis.Scripting.resources.dll => 385
	i64 u0x2e2ced2c3c6a1edc, ; 242: lib_System.Threading.AccessControl.dll.so => 142
	i64 u0x2e4d2e03e610a6e9, ; 243: pl/Microsoft.CodeAnalysis.resources => 348
	i64 u0x2e5a40c319acb800, ; 244: System.IO.FileSystem => 51
	i64 u0x2e6f1f226821322a, ; 245: el/Microsoft.Maui.Controls.resources.dll => 398
	i64 u0x2e8ff3fae87a8245, ; 246: lib_Microsoft.JSInterop.dll.so => 229
	i64 u0x2f02f94df3200fe5, ; 247: System.Diagnostics.Process => 29
	i64 u0x2f2e98e1c89b1aff, ; 248: System.Xml.ReaderWriter => 160
	i64 u0x2f5911d9ba814e4e, ; 249: System.Diagnostics.Tracing => 34
	i64 u0x2f84070a459bc31f, ; 250: lib_System.Xml.dll.so => 167
	i64 u0x2feb4d2fcda05cfd, ; 251: Microsoft.Extensions.Caching.Abstractions.dll => 199
	i64 u0x2ff49de6a71764a1, ; 252: lib_Microsoft.Extensions.Http.dll.so => 217
	i64 u0x309ee9eeec09a71e, ; 253: lib_Xamarin.AndroidX.Fragment.dll.so => 282
	i64 u0x309f2bedefa9a318, ; 254: Microsoft.IdentityModel.Abstractions => 225
	i64 u0x30c6dda129408828, ; 255: System.IO.IsolatedStorage => 52
	i64 u0x310d9651ec86c411, ; 256: Microsoft.Extensions.FileProviders.Embedded => 213
	i64 u0x31195fef5d8fb552, ; 257: _Microsoft.Android.Resource.Designer.dll => 435
	i64 u0x312c8ed623cbfc8d, ; 258: Xamarin.AndroidX.Window.dll => 325
	i64 u0x31496b779ed0663d, ; 259: lib_System.Reflection.DispatchProxy.dll.so => 92
	i64 u0x31962e0f7c634c16, ; 260: Polly.Core => 237
	i64 u0x32243413e774362a, ; 261: Xamarin.AndroidX.CardView.dll => 263
	i64 u0x3235427f8d12dae1, ; 262: lib_System.Drawing.Primitives.dll.so => 35
	i64 u0x324622a9fd95b0c8, ; 263: lib-cs-Microsoft.CodeAnalysis.resources.dll.so => 341
	i64 u0x326256f7722d4fe5, ; 264: SkiaSharp.Views.Maui.Controls.dll => 241
	i64 u0x329753a17a517811, ; 265: fr/Microsoft.Maui.Controls.resources => 401
	i64 u0x32aa989ff07a84ff, ; 266: lib_System.Xml.ReaderWriter.dll.so => 160
	i64 u0x33642d5508314e46, ; 267: Microsoft.Extensions.FileSystemGlobbing.dll => 215
	i64 u0x33829542f112d59b, ; 268: System.Collections.Immutable => 9
	i64 u0x33a31443733849fe, ; 269: lib-es-Microsoft.Maui.Controls.resources.dll.so => 399
	i64 u0x341abc357fbb4ebf, ; 270: lib_System.Net.Sockets.dll.so => 78
	i64 u0x346a212343615ac5, ; 271: lib_System.Linq.AsyncEnumerable.dll.so => 59
	i64 u0x3496a1fbc5b6330d, ; 272: es/Microsoft.CodeAnalysis.Scripting.resources => 382
	i64 u0x3496c1e2dcaf5ecc, ; 273: lib_System.IO.Pipes.AccessControl.dll.so => 55
	i64 u0x34bd01fd4be06ee3, ; 274: lib_Microsoft.Extensions.FileProviders.Composite.dll.so => 212
	i64 u0x34dfd74fe2afcf37, ; 275: Microsoft.Maui => 232
	i64 u0x34e292762d9615df, ; 276: cs/Microsoft.Maui.Controls.resources.dll => 395
	i64 u0x34ef56e1435b2843, ; 277: pl/Microsoft.CodeAnalysis.CSharp.resources.dll => 361
	i64 u0x3508234247f48404, ; 278: Microsoft.Maui.Controls => 230
	i64 u0x353590da528c9d22, ; 279: System.ComponentModel.Annotations => 13
	i64 u0x3549870798b4cd30, ; 280: lib_Xamarin.AndroidX.ViewPager2.dll.so => 324
	i64 u0x355282fc1c909694, ; 281: Microsoft.Extensions.Configuration => 201
	i64 u0x3552fc5d578f0fbf, ; 282: Xamarin.AndroidX.Arch.Core.Common => 260
	i64 u0x355c649948d55d97, ; 283: lib_System.Runtime.Intrinsics.dll.so => 111
	i64 u0x35766456ffb7a7b4, ; 284: fr/Microsoft.CodeAnalysis.CSharp.resources.dll => 357
	i64 u0x35ea9d1c6834bc8c, ; 285: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll => 297
	i64 u0x3628ab68db23a01a, ; 286: lib_System.Diagnostics.Tools.dll.so => 32
	i64 u0x3673b042508f5b6b, ; 287: lib_System.Runtime.Extensions.dll.so => 106
	i64 u0x36740f1a8ecdc6c4, ; 288: System.Numerics => 86
	i64 u0x36b2b50fdf589ae2, ; 289: System.Reflection.Emit.Lightweight => 94
	i64 u0x36cada77dc79928b, ; 290: System.IO.MemoryMappedFiles => 53
	i64 u0x374ef46b06791af6, ; 291: System.Reflection.Primitives.dll => 98
	i64 u0x376bf93e521a5417, ; 292: lib_Xamarin.Jetbrains.Annotations.dll.so => 333
	i64 u0x37bc29f3183003b6, ; 293: lib_System.IO.dll.so => 58
	i64 u0x380134e03b1e160a, ; 294: System.Collections.Immutable.dll => 9
	i64 u0x38049b5c59b39324, ; 295: System.Runtime.CompilerServices.Unsafe => 104
	i64 u0x385c17636bb6fe6e, ; 296: Xamarin.AndroidX.CustomView.dll => 275
	i64 u0x38869c811d74050e, ; 297: System.Net.NameResolution.dll => 69
	i64 u0x393c226616977fdb, ; 298: lib_Xamarin.AndroidX.ViewPager.dll.so => 323
	i64 u0x395b3053dde89e41, ; 299: lib_System.Reactive.dll.so => 248
	i64 u0x395e37c3334cf82a, ; 300: lib-ca-Microsoft.Maui.Controls.resources.dll.so => 394
	i64 u0x39721dd6cab9d79e, ; 301: Polly.dll => 236
	i64 u0x39a87563fdb248a0, ; 302: System.Reactive.dll => 248
	i64 u0x39aa39fda111d9d3, ; 303: Newtonsoft.Json => 235
	i64 u0x39c3107c28752af1, ; 304: lib_Microsoft.Extensions.FileProviders.Abstractions.dll.so => 211
	i64 u0x3ab5859054645f72, ; 305: System.Security.Cryptography.Primitives.dll => 127
	i64 u0x3ad75090c3fac0e9, ; 306: lib_Xamarin.AndroidX.ResourceInspection.Annotation.dll.so => 309
	i64 u0x3ae44ac43a1fbdbb, ; 307: System.Runtime.Serialization => 118
	i64 u0x3b860f9932505633, ; 308: lib_System.Text.Encoding.Extensions.dll.so => 137
	i64 u0x3be6248c2bc7dc8c, ; 309: Microsoft.JSInterop.dll => 229
	i64 u0x3be99b43dd39dd37, ; 310: Xamarin.AndroidX.SavedState.SavedState.Android => 311
	i64 u0x3bea9ebe8c027c01, ; 311: lib_Microsoft.IdentityModel.Tokens.dll.so => 228
	i64 u0x3c3aafb6b3a00bf6, ; 312: lib_System.Security.Cryptography.X509Certificates.dll.so => 128
	i64 u0x3c4049146b59aa90, ; 313: System.Runtime.InteropServices.JavaScript => 108
	i64 u0x3c708e9b7a0ff300, ; 314: ko/Microsoft.CodeAnalysis.Scripting.resources => 386
	i64 u0x3c7c495f58ac5ee9, ; 315: Xamarin.Kotlin.StdLib => 335
	i64 u0x3c7e5ed3d5db71bb, ; 316: System.Security => 133
	i64 u0x3cd9d281d402eb9b, ; 317: Xamarin.AndroidX.Browser.dll => 262
	i64 u0x3d1c50cc001a991e, ; 318: Xamarin.Google.Guava.ListenableFuture.dll => 332
	i64 u0x3d2b1913edfc08d7, ; 319: lib_System.Threading.ThreadPool.dll.so => 150
	i64 u0x3d46f0b995082740, ; 320: System.Xml.Linq => 159
	i64 u0x3d8a8f400514a790, ; 321: Xamarin.AndroidX.Fragment.Ktx.dll => 283
	i64 u0x3d9c2a242b040a50, ; 322: lib_Xamarin.AndroidX.Core.dll.so => 271
	i64 u0x3da7781d6333a8fe, ; 323: SQLitePCLRaw.batteries_v2 => 243
	i64 u0x3db495de2204755c, ; 324: Microsoft.Extensions.Configuration.FileExtensions => 204
	i64 u0x3dbb6b9f5ab90fa7, ; 325: lib_Xamarin.AndroidX.DynamicAnimation.dll.so => 278
	i64 u0x3e5441657549b213, ; 326: Xamarin.AndroidX.ResourceInspection.Annotation => 309
	i64 u0x3e57d4d195c53c2e, ; 327: System.Reflection.TypeExtensions => 99
	i64 u0x3e616ab4ed1f3f15, ; 328: lib_System.Data.dll.so => 24
	i64 u0x3e7f8912b96e5065, ; 329: Microsoft.AspNetCore.Components.WebView.dll => 187
	i64 u0x3f1d226e6e06db7e, ; 330: Xamarin.AndroidX.SlidingPaneLayout.dll => 314
	i64 u0x3f510adf788828dd, ; 331: System.Threading.Tasks.Extensions => 146
	i64 u0x3f6f5914291cdcf7, ; 332: Microsoft.Extensions.Hosting.Abstractions => 216
	i64 u0x407a10bb4bf95829, ; 333: lib_Xamarin.AndroidX.Navigation.Common.dll.so => 301
	i64 u0x40c6d9cbfdb8b9f7, ; 334: SkiaSharp.Views.Maui.Core.dll => 242
	i64 u0x40c98b6bd77346d4, ; 335: Microsoft.VisualBasic.dll => 3
	i64 u0x415c502eb40e7418, ; 336: es/Microsoft.CodeAnalysis.resources.dll => 343
	i64 u0x41833cf766d27d96, ; 337: mscorlib => 170
	i64 u0x41986970eb721f2b, ; 338: Microsoft.CodeAnalysis.Scripting.dll => 193
	i64 u0x41cab042be111c34, ; 339: lib_Xamarin.AndroidX.AppCompat.AppCompatResources.dll.so => 259
	i64 u0x423a9ecc4d905a88, ; 340: lib_System.Resources.ResourceManager.dll.so => 102
	i64 u0x423bf51ae7def810, ; 341: System.Xml.XPath => 164
	i64 u0x42462ff15ddba223, ; 342: System.Resources.Reader.dll => 101
	i64 u0x4260fbb8d8266ed7, ; 343: AccessibleTrader.Sdk.dll => 434
	i64 u0x4291015ff4e5ef71, ; 344: Xamarin.AndroidX.Core.ViewTree.dll => 273
	i64 u0x42a31b86e6ccc3f0, ; 345: System.Diagnostics.Contracts => 25
	i64 u0x430e95b891249788, ; 346: lib_System.Reflection.Emit.dll.so => 95
	i64 u0x43375950ec7c1b6a, ; 347: netstandard.dll => 171
	i64 u0x434c4e1d9284cdae, ; 348: Mono.Android.dll => 175
	i64 u0x43505013578652a0, ; 349: lib_Xamarin.AndroidX.Activity.Ktx.dll.so => 254
	i64 u0x437d06c381ed575a, ; 350: lib_Microsoft.VisualBasic.dll.so => 3
	i64 u0x43950f84de7cc79a, ; 351: pl/Microsoft.Maui.Controls.resources.dll => 413
	i64 u0x43c077442b230f64, ; 352: Xamarin.AndroidX.Tracing.Tracing.Android => 318
	i64 u0x43e8ca5bc927ff37, ; 353: lib_Xamarin.AndroidX.Emoji2.ViewsHelper.dll.so => 280
	i64 u0x4454e6d0b361addc, ; 354: lib-ru-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 376
	i64 u0x4489438e98f9c0f7, ; 355: AccessibleTrader.Plugins.Coinbase.dll => 431
	i64 u0x448bd33429269b19, ; 356: Microsoft.CSharp => 1
	i64 u0x4499fa3c8e494654, ; 357: lib_System.Runtime.Serialization.Primitives.dll.so => 116
	i64 u0x4515080865a951a5, ; 358: Xamarin.Kotlin.StdLib.dll => 335
	i64 u0x453c1277f85cf368, ; 359: lib_Microsoft.EntityFrameworkCore.Abstractions.dll.so => 196
	i64 u0x4545802489b736b9, ; 360: Xamarin.AndroidX.Fragment.Ktx => 283
	i64 u0x454b4d1e66bb783c, ; 361: Xamarin.AndroidX.Lifecycle.Process => 290
	i64 u0x458d2df79ac57c1d, ; 362: lib_System.IdentityModel.Tokens.Jwt.dll.so => 247
	i64 u0x45c40276a42e283e, ; 363: System.Diagnostics.TraceSource => 33
	i64 u0x45d443f2a29adc37, ; 364: System.AppContext.dll => 6
	i64 u0x45fcc9fd66f25095, ; 365: Microsoft.Extensions.DependencyModel => 208
	i64 u0x46a4213bc97fe5ae, ; 366: lib-ru-Microsoft.Maui.Controls.resources.dll.so => 417
	i64 u0x47358bd471172e1d, ; 367: lib_System.Xml.Linq.dll.so => 159
	i64 u0x475461b41cd2bae5, ; 368: lib-zh-Hant-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 366
	i64 u0x47aaf174699667d9, ; 369: tr/Microsoft.CodeAnalysis.Scripting.resources.dll => 390
	i64 u0x47daf4e1afbada10, ; 370: pt/Microsoft.Maui.Controls.resources => 415
	i64 u0x480c0a47dd42dd81, ; 371: lib_System.IO.MemoryMappedFiles.dll.so => 53
	i64 u0x49e952f19a4e2022, ; 372: System.ObjectModel => 87
	i64 u0x49f9e6948a8131e4, ; 373: lib_Xamarin.AndroidX.VersionedParcelable.dll.so => 322
	i64 u0x4a1afd3bf9c69c98, ; 374: fr/Microsoft.CodeAnalysis.resources => 344
	i64 u0x4a5667b2462a664b, ; 375: lib_Xamarin.AndroidX.Navigation.UI.dll.so => 306
	i64 u0x4a7a18981dbd56bc, ; 376: System.IO.Compression.FileSystem.dll => 44
	i64 u0x4aa5c60350917c06, ; 377: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll.so => 289
	i64 u0x4aaf23876d7335cb, ; 378: lib-zh-Hant-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 379
	i64 u0x4b07a0ed0ab33ff4, ; 379: System.Runtime.Extensions.dll => 106
	i64 u0x4b484a0d637947d7, ; 380: lib-zh-Hans-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 365
	i64 u0x4b558744a6e1abe0, ; 381: lib-de-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 355
	i64 u0x4b576d47ac054f3c, ; 382: System.IO.FileSystem.AccessControl => 47
	i64 u0x4b7b6532ded934b7, ; 383: System.Text.Json => 140
	i64 u0x4bf547f87e5016a8, ; 384: lib_SkiaSharp.Views.Android.dll.so => 240
	i64 u0x4c2029a97af23a8d, ; 385: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android => 299
	i64 u0x4c7755cf07ad2d5f, ; 386: System.Net.Http.Json.dll => 65
	i64 u0x4ca014ceac582c86, ; 387: Microsoft.EntityFrameworkCore.Relational.dll => 197
	i64 u0x4cc5f15266470798, ; 388: lib_Xamarin.AndroidX.Loader.dll.so => 300
	i64 u0x4cf6f67dc77aacd2, ; 389: System.Net.NetworkInformation.dll => 70
	i64 u0x4d3183dd245425d4, ; 390: System.Net.WebSockets.Client.dll => 82
	i64 u0x4d479f968a05e504, ; 391: System.Linq.Expressions.dll => 60
	i64 u0x4d55a010ffc4faff, ; 392: System.Private.Xml => 91
	i64 u0x4d5cbe77561c5b2e, ; 393: System.Web.dll => 157
	i64 u0x4d77512dbd86ee4c, ; 394: lib_Xamarin.AndroidX.Arch.Core.Common.dll.so => 260
	i64 u0x4d7793536e79c309, ; 395: System.ServiceProcess => 135
	i64 u0x4d95fccc1f67c7ca, ; 396: System.Runtime.Loader.dll => 112
	i64 u0x4db014bf0ff1c9c1, ; 397: System.Linq.AsyncEnumerable => 59
	i64 u0x4dcf44c3c9b076a2, ; 398: it/Microsoft.Maui.Controls.resources.dll => 407
	i64 u0x4dd9247f1d2c3235, ; 399: Xamarin.AndroidX.Loader.dll => 300
	i64 u0x4df510084e2a0bae, ; 400: Microsoft.JSInterop => 229
	i64 u0x4e2aeee78e2c4a87, ; 401: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller => 307
	i64 u0x4e32f00cb0937401, ; 402: Mono.Android.Runtime => 174
	i64 u0x4e5eea4668ac2b18, ; 403: System.Text.Encoding.CodePages => 136
	i64 u0x4e84220084ab2d20, ; 404: cs/Microsoft.CodeAnalysis.CSharp.resources.dll => 354
	i64 u0x4ebd0c4b82c5eefc, ; 405: lib_System.Threading.Channels.dll.so => 143
	i64 u0x4ee8eaa9c9c1151a, ; 406: System.Globalization.Calendars => 40
	i64 u0x4f21ee6ef9eb527e, ; 407: ca/Microsoft.Maui.Controls.resources => 394
	i64 u0x4fd5f3ee53d0a4f0, ; 408: SQLitePCLRaw.lib.e_sqlite3.android => 245
	i64 u0x4fdc964ec1888e25, ; 409: lib_Microsoft.Extensions.Configuration.Binder.dll.so => 203
	i64 u0x4ffd65baff757598, ; 410: Microsoft.IdentityModel.Tokens => 228
	i64 u0x5037f0be3c28c7a3, ; 411: lib_Microsoft.Maui.Controls.dll.so => 230
	i64 u0x50c3a29b21050d45, ; 412: System.Linq.Parallel.dll => 61
	i64 u0x5112ed116d87baf8, ; 413: CommunityToolkit.Mvvm => 179
	i64 u0x5116b21580ae6eb0, ; 414: Microsoft.Extensions.Configuration.Binder.dll => 203
	i64 u0x5131bbe80989093f, ; 415: Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll => 296
	i64 u0x516324a5050a7e3c, ; 416: System.Net.WebProxy => 81
	i64 u0x516d6f0b21a303de, ; 417: lib_System.Diagnostics.Contracts.dll.so => 25
	i64 u0x51bb8a2afe774e32, ; 418: System.Drawing => 36
	i64 u0x5247c5c32a4140f0, ; 419: System.Resources.Reader => 101
	i64 u0x526bb15e3c386364, ; 420: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll => 293
	i64 u0x526ce79eb8e90527, ; 421: lib_System.Net.Primitives.dll.so => 72
	i64 u0x52808a6faff1589d, ; 422: zh-Hant/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 379
	i64 u0x52829f00b4467c38, ; 423: lib_System.Data.Common.dll.so => 22
	i64 u0x529ffe06f39ab8db, ; 424: Xamarin.AndroidX.Core => 271
	i64 u0x52ff996554dbf352, ; 425: Microsoft.Maui.Graphics => 234
	i64 u0x533514f6711b299b, ; 426: ko/Microsoft.CodeAnalysis.CSharp.resources => 360
	i64 u0x535f7e40e8fef8af, ; 427: lib-sk-Microsoft.Maui.Controls.resources.dll.so => 418
	i64 u0x53978aac584c666e, ; 428: lib_System.Security.Cryptography.Cng.dll.so => 123
	i64 u0x539dd21b5958055b, ; 429: AccessibleTrader.Plugins.Alpaca => 428
	i64 u0x53a96d5c86c9e194, ; 430: System.Net.NetworkInformation => 70
	i64 u0x53be1038a61e8d44, ; 431: System.Runtime.InteropServices.RuntimeInformation.dll => 109
	i64 u0x53c3014b9437e684, ; 432: lib-zh-HK-Microsoft.Maui.Controls.resources.dll.so => 424
	i64 u0x5435e6f049e9bc37, ; 433: System.Security.Claims.dll => 121
	i64 u0x54795225dd1587af, ; 434: lib_System.Runtime.dll.so => 119
	i64 u0x547a34f14e5f6210, ; 435: Xamarin.AndroidX.Lifecycle.Common.dll => 285
	i64 u0x54b851bc9b470503, ; 436: Xamarin.AndroidX.Navigation.Common.Android => 302
	i64 u0x54d75f85d6578cff, ; 437: lib-fr-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 357
	i64 u0x556e8b63b660ab8b, ; 438: Xamarin.AndroidX.Lifecycle.Common.Jvm.dll => 286
	i64 u0x5588627c9a108ec9, ; 439: System.Collections.Specialized => 11
	i64 u0x55a898e4f42e3fae, ; 440: Microsoft.VisualBasic.Core.dll => 2
	i64 u0x55fa0c610fe93bb1, ; 441: lib_System.Security.Cryptography.OpenSsl.dll.so => 126
	i64 u0x561449e1215a61e4, ; 442: lib_SkiaSharp.Views.Maui.Core.dll.so => 242
	i64 u0x56442b99bc64bb47, ; 443: System.Runtime.Serialization.Xml.dll => 117
	i64 u0x56a8b26e1aeae27b, ; 444: System.Threading.Tasks.Dataflow => 145
	i64 u0x56f76b6edb837f8b, ; 445: Polly => 236
	i64 u0x56f932d61e93c07f, ; 446: System.Globalization.Extensions => 41
	i64 u0x571c5cfbec5ae8e2, ; 447: System.Private.Uri => 89
	i64 u0x5724fbe6b45b7f07, ; 448: lib-pt-BR-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 362
	i64 u0x573a4253c76b0960, ; 449: AccessibleTrader.Plugins.Polygon => 433
	i64 u0x576499c9f52fea31, ; 450: Xamarin.AndroidX.Annotation => 255
	i64 u0x578cd35c91d7b347, ; 451: lib_SQLitePCLRaw.core.dll.so => 244
	i64 u0x579a06fed6eec900, ; 452: System.Private.CoreLib.dll => 177
	i64 u0x57adda3c951abb33, ; 453: Microsoft.Extensions.Hosting.Abstractions.dll => 216
	i64 u0x57c542c14049b66d, ; 454: System.Diagnostics.DiagnosticSource => 27
	i64 u0x581a8bd5cfda563e, ; 455: System.Threading.Timer => 151
	i64 u0x582046896c5b64ca, ; 456: lib-fr-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 370
	i64 u0x584ac38e21d2fde1, ; 457: Microsoft.Extensions.Configuration.Binder => 203
	i64 u0x58601b2dda4a27b9, ; 458: lib-ja-Microsoft.Maui.Controls.resources.dll.so => 408
	i64 u0x58688d9af496b168, ; 459: Microsoft.Extensions.DependencyInjection.dll => 206
	i64 u0x588c167a79db6bfb, ; 460: lib_Xamarin.Google.ErrorProne.Annotations.dll.so => 331
	i64 u0x58ef0576630aa114, ; 461: fr/Microsoft.CodeAnalysis.CSharp.resources => 357
	i64 u0x5906028ae5151104, ; 462: Xamarin.AndroidX.Activity.Ktx => 254
	i64 u0x595a356d23e8da9a, ; 463: lib_Microsoft.CSharp.dll.so => 1
	i64 u0x59f9e60b9475085f, ; 464: lib_Xamarin.AndroidX.Annotation.Experimental.dll.so => 256
	i64 u0x5a745f5101a75527, ; 465: lib_System.IO.Compression.FileSystem.dll.so => 44
	i64 u0x5a89a886ae30258d, ; 466: lib_Xamarin.AndroidX.CoordinatorLayout.dll.so => 270
	i64 u0x5a8f6699f4a1caa9, ; 467: lib_System.Threading.dll.so => 152
	i64 u0x5ab8ea45990c0132, ; 468: lib_AccessibleTrader.Plugins.Bitstamp.dll.so => 430
	i64 u0x5ae9cd33b15841bf, ; 469: System.ComponentModel => 18
	i64 u0x5b2384937958c860, ; 470: zh-Hant/Microsoft.CodeAnalysis.Scripting.resources => 392
	i64 u0x5b54391bdc6fcfe6, ; 471: System.Private.DataContractSerialization => 88
	i64 u0x5b5ba1327561f926, ; 472: lib_SkiaSharp.Views.Maui.Controls.dll.so => 241
	i64 u0x5b5f0e240a06a2a2, ; 473: da/Microsoft.Maui.Controls.resources.dll => 396
	i64 u0x5b8109e8e14c5e3e, ; 474: System.Globalization.Extensions.dll => 41
	i64 u0x5bb93c3ef9525c89, ; 475: es/Microsoft.CodeAnalysis.resources => 343
	i64 u0x5bddd04d72a9e350, ; 476: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx => 289
	i64 u0x5bdf16b09da116ab, ; 477: Xamarin.AndroidX.Collection => 264
	i64 u0x5be34cb3cc2ff949, ; 478: tr/Microsoft.CodeAnalysis.CSharp.resources => 364
	i64 u0x5c019d5266093159, ; 479: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll.so => 294
	i64 u0x5c30a4a35f9cc8c4, ; 480: lib_System.Reflection.Extensions.dll.so => 96
	i64 u0x5c393624b8176517, ; 481: lib_Microsoft.Extensions.Logging.dll.so => 218
	i64 u0x5c53c29f5073b0c9, ; 482: System.Diagnostics.FileVersionInfo => 28
	i64 u0x5c6724284a5e7317, ; 483: lib-tr-Microsoft.CodeAnalysis.resources.dll.so => 351
	i64 u0x5c87463c575c7616, ; 484: lib_System.Globalization.Extensions.dll.so => 41
	i64 u0x5d0a4a29b02d9d3c, ; 485: System.Net.WebHeaderCollection.dll => 80
	i64 u0x5d25ef991dd9a85c, ; 486: Microsoft.AspNetCore.Components.WebView.Maui.dll => 188
	i64 u0x5d40c9b15181641f, ; 487: lib_Xamarin.AndroidX.Emoji2.dll.so => 279
	i64 u0x5d6ca10d35e9485b, ; 488: lib_Xamarin.AndroidX.Concurrent.Futures.dll.so => 267
	i64 u0x5d7ec76c1c703055, ; 489: System.Threading.Tasks.Parallel => 147
	i64 u0x5db0cbbd1028510e, ; 490: lib_System.Runtime.InteropServices.dll.so => 110
	i64 u0x5db30905d3e5013b, ; 491: Xamarin.AndroidX.Collection.Jvm.dll => 265
	i64 u0x5e467bc8f09ad026, ; 492: System.Collections.Specialized.dll => 11
	i64 u0x5e5173b3208d97e7, ; 493: System.Runtime.Handles.dll => 107
	i64 u0x5e8eb5167db0428b, ; 494: es/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 369
	i64 u0x5ea92fdb19ec8c4c, ; 495: System.Text.Encodings.Web.dll => 139
	i64 u0x5eb8046dd40e9ac3, ; 496: System.ComponentModel.Primitives => 16
	i64 u0x5ec272d219c9aba4, ; 497: System.Security.Cryptography.Csp.dll => 124
	i64 u0x5eee1376d94c7f5e, ; 498: System.Net.HttpListener.dll => 67
	i64 u0x5f36ccf5c6a57e24, ; 499: System.Xml.ReaderWriter.dll => 160
	i64 u0x5f4294b9b63cb842, ; 500: System.Data.Common => 22
	i64 u0x5f7399e166075632, ; 501: lib_SQLitePCLRaw.lib.e_sqlite3.android.dll.so => 245
	i64 u0x5f9a2d823f664957, ; 502: lib-el-Microsoft.Maui.Controls.resources.dll.so => 398
	i64 u0x5fa6da9c3cd8142a, ; 503: lib_Xamarin.KotlinX.Serialization.Core.dll.so => 339
	i64 u0x5fac98e0b37a5b9d, ; 504: System.Runtime.CompilerServices.Unsafe.dll => 104
	i64 u0x609f4b7b63d802d4, ; 505: lib_Microsoft.Extensions.DependencyInjection.dll.so => 206
	i64 u0x60cd4e33d7e60134, ; 506: Xamarin.KotlinX.Coroutines.Core.Jvm => 338
	i64 u0x60f62d786afcf130, ; 507: System.Memory => 64
	i64 u0x61bb78c89f867353, ; 508: System.IO => 58
	i64 u0x61be8d1299194243, ; 509: Microsoft.Maui.Controls.Xaml => 231
	i64 u0x61d2cba29557038f, ; 510: de/Microsoft.Maui.Controls.resources => 397
	i64 u0x61d88f399afb2f45, ; 511: lib_System.Runtime.Loader.dll.so => 112
	i64 u0x622eef6f9e59068d, ; 512: System.Private.CoreLib => 177
	i64 u0x62bffe14414fd6ca, ; 513: lib-zh-Hans-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 391
	i64 u0x6314e018e86b4f94, ; 514: AccessibleTrader.Plugins.Binance => 429
	i64 u0x639fb99a7bef11de, ; 515: Xamarin.AndroidX.Navigation.Runtime.Android.dll => 305
	i64 u0x63d5e3aa4ef9b931, ; 516: Xamarin.KotlinX.Coroutines.Android.dll => 336
	i64 u0x63f1f6883c1e23c2, ; 517: lib_System.Collections.Immutable.dll.so => 9
	i64 u0x6400f68068c1e9f1, ; 518: Xamarin.Google.Android.Material.dll => 328
	i64 u0x640e3b14dbd325c2, ; 519: System.Security.Cryptography.Algorithms.dll => 122
	i64 u0x64587004560099b9, ; 520: System.Reflection => 100
	i64 u0x64b1529a438a3c45, ; 521: lib_System.Runtime.Handles.dll.so => 107
	i64 u0x64b61dd9da8a4d57, ; 522: System.Net.ServerSentEvents.dll => 76
	i64 u0x6514d0e310a70ab0, ; 523: ja/Microsoft.CodeAnalysis.Scripting.resources => 385
	i64 u0x6565fba2cd8f235b, ; 524: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx => 297
	i64 u0x65d8ddec9a3de89e, ; 525: ru/Microsoft.CodeAnalysis.resources => 350
	i64 u0x65ecac39144dd3cc, ; 526: Microsoft.Maui.Controls.dll => 230
	i64 u0x65ece51227bfa724, ; 527: lib_System.Runtime.Numerics.dll.so => 113
	i64 u0x661722438787b57f, ; 528: Xamarin.AndroidX.Annotation.Jvm.dll => 257
	i64 u0x6679b2337ee6b22a, ; 529: lib_System.IO.FileSystem.Primitives.dll.so => 49
	i64 u0x667c66a03dd97d40, ; 530: System.Linq.AsyncEnumerable.dll => 59
	i64 u0x6692e924eade1b29, ; 531: lib_System.Console.dll.so => 20
	i64 u0x66a4e5c6a3fb0bae, ; 532: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll.so => 296
	i64 u0x66d13304ce1a3efa, ; 533: Xamarin.AndroidX.CursorAdapter => 274
	i64 u0x674303f65d8fad6f, ; 534: lib_System.Net.Quic.dll.so => 73
	i64 u0x6756ca4cad62e9d6, ; 535: lib_Xamarin.AndroidX.ConstraintLayout.Core.dll.so => 269
	i64 u0x67c0802770244408, ; 536: System.Windows.dll => 158
	i64 u0x68100b69286e27cd, ; 537: lib_System.Formats.Tar.dll.so => 39
	i64 u0x68558ec653afa616, ; 538: lib-da-Microsoft.Maui.Controls.resources.dll.so => 396
	i64 u0x6857d56b8e8b4bb6, ; 539: lib_Microsoft.AspNetCore.Metadata.dll.so => 189
	i64 u0x6872ec7a2e36b1ac, ; 540: System.Drawing.Primitives.dll => 35
	i64 u0x68fbbbe2eb455198, ; 541: System.Formats.Asn1 => 38
	i64 u0x69063fc0ba8e6bdd, ; 542: he/Microsoft.Maui.Controls.resources.dll => 402
	i64 u0x699dffb2427a2d71, ; 543: SQLitePCLRaw.lib.e_sqlite3.android.dll => 245
	i64 u0x69c43767b6624bb2, ; 544: pl/Microsoft.CodeAnalysis.CSharp.resources => 361
	i64 u0x6a4d7577b2317255, ; 545: System.Runtime.InteropServices.dll => 110
	i64 u0x6abfbfb2796f4e84, ; 546: Microsoft.CodeAnalysis.CSharp => 191
	i64 u0x6ace3b74b15ee4a4, ; 547: nb/Microsoft.Maui.Controls.resources => 411
	i64 u0x6afcedb171067e2b, ; 548: System.Core.dll => 21
	i64 u0x6b613586f98094ed, ; 549: it/Microsoft.CodeAnalysis.Scripting.resources.dll => 384
	i64 u0x6bef98e124147c24, ; 550: Xamarin.Jetbrains.Annotations => 333
	i64 u0x6c475a83367e34db, ; 551: ko/Microsoft.CodeAnalysis.Scripting.resources.dll => 386
	i64 u0x6ce874bff138ce2b, ; 552: Xamarin.AndroidX.Lifecycle.ViewModel.dll => 295
	i64 u0x6d12bfaa99c72b1f, ; 553: lib_Microsoft.Maui.Graphics.dll.so => 234
	i64 u0x6d70755158ca866e, ; 554: lib_System.ComponentModel.EventBasedAsync.dll.so => 15
	i64 u0x6d79993361e10ef2, ; 555: Microsoft.Extensions.Primitives => 223
	i64 u0x6d7eeca99577fc8b, ; 556: lib_System.Net.WebProxy.dll.so => 81
	i64 u0x6d8515b19946b6a2, ; 557: System.Net.WebProxy.dll => 81
	i64 u0x6d86d56b84c8eb71, ; 558: lib_Xamarin.AndroidX.CursorAdapter.dll.so => 274
	i64 u0x6d9bea6b3e895cf7, ; 559: Microsoft.Extensions.Primitives.dll => 223
	i64 u0x6e145bcac443aa70, ; 560: pt-BR/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 375
	i64 u0x6e25a02c3833319a, ; 561: lib_Xamarin.AndroidX.Navigation.Fragment.dll.so => 303
	i64 u0x6e2fb2ace98ab808, ; 562: zh-Hant/Microsoft.CodeAnalysis.CSharp.resources => 366
	i64 u0x6e79c6bd8627412a, ; 563: Xamarin.AndroidX.SavedState.SavedState.Ktx => 312
	i64 u0x6e838d9a2a6f6c9e, ; 564: lib_System.ValueTuple.dll.so => 155
	i64 u0x6e9965ce1095e60a, ; 565: lib_System.Core.dll.so => 21
	i64 u0x6ed5cb5878424ad5, ; 566: pl/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 374
	i64 u0x6fa7ed9b35fd3dd3, ; 567: DynamicData => 181
	i64 u0x6fd2265da78b93a4, ; 568: lib_Microsoft.Maui.dll.so => 232
	i64 u0x6fdfc7de82c33008, ; 569: cs/Microsoft.Maui.Controls.resources => 395
	i64 u0x6ffc4967cc47ba57, ; 570: System.IO.FileSystem.Watcher.dll => 50
	i64 u0x701cd46a1c25a5fe, ; 571: System.IO.FileSystem.dll => 51
	i64 u0x7078c940a89ab2ee, ; 572: ja/Microsoft.CodeAnalysis.CSharp.resources => 359
	i64 u0x70d67fb826a6921f, ; 573: Microsoft.Extensions.Validation.dll => 224
	i64 u0x70e99f48c05cb921, ; 574: tr/Microsoft.Maui.Controls.resources.dll => 421
	i64 u0x70fd3deda22442d2, ; 575: lib-nb-Microsoft.Maui.Controls.resources.dll.so => 411
	i64 u0x71485e7ffdb4b958, ; 576: System.Reflection.Extensions => 96
	i64 u0x7162a2fce67a945f, ; 577: lib_Xamarin.Android.Glide.Annotations.dll.so => 250
	i64 u0x717530326f808838, ; 578: lib_Microsoft.Extensions.Diagnostics.Abstractions.dll.so => 210
	i64 u0x71a248f3a13ad366, ; 579: tr/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 377
	i64 u0x71a495ea3761dde8, ; 580: lib-it-Microsoft.Maui.Controls.resources.dll.so => 407
	i64 u0x71ad672adbe48f35, ; 581: System.ComponentModel.Primitives.dll => 16
	i64 u0x720f102581a4a5c8, ; 582: Xamarin.AndroidX.Core.ViewTree => 273
	i64 u0x725f5a9e82a45c81, ; 583: System.Security.Cryptography.Encoding => 125
	i64 u0x72b1fb4109e08d7b, ; 584: lib-hr-Microsoft.Maui.Controls.resources.dll.so => 404
	i64 u0x72e0300099accce1, ; 585: System.Xml.XPath.XDocument => 163
	i64 u0x730bfb248998f67a, ; 586: System.IO.Compression.ZipFile => 45
	i64 u0x732b2d67b9e5c47b, ; 587: Xamarin.Google.ErrorProne.Annotations.dll => 331
	i64 u0x734b76fdc0dc05bb, ; 588: lib_GoogleGson.dll.so => 182
	i64 u0x73a6be34e822f9d1, ; 589: lib_System.Runtime.Serialization.dll.so => 118
	i64 u0x73e4ce94e2eb6ffc, ; 590: lib_System.Memory.dll.so => 64
	i64 u0x73f2645914262879, ; 591: lib_Microsoft.EntityFrameworkCore.Sqlite.dll.so => 198
	i64 u0x743a1eccf080489a, ; 592: WindowsBase.dll => 169
	i64 u0x7444749b0fe2993c, ; 593: lib-fr-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 383
	i64 u0x746cf89b511b4d40, ; 594: lib_Microsoft.Extensions.Diagnostics.dll.so => 209
	i64 u0x755a91767330b3d4, ; 595: lib_Microsoft.Extensions.Configuration.dll.so => 201
	i64 u0x75c326eb821b85c4, ; 596: lib_System.ComponentModel.DataAnnotations.dll.so => 14
	i64 u0x76012e7334db86e5, ; 597: lib_Xamarin.AndroidX.SavedState.dll.so => 310
	i64 u0x76ca07b878f44da0, ; 598: System.Runtime.Numerics.dll => 113
	i64 u0x7736c8a96e51a061, ; 599: lib_Xamarin.AndroidX.Annotation.Jvm.dll.so => 257
	i64 u0x778a805e625329ef, ; 600: System.Linq.Parallel => 61
	i64 u0x779f67ad3b8efbd5, ; 601: Microsoft.Extensions.Configuration.Json.dll => 205
	i64 u0x77d9074d8f33a303, ; 602: lib_System.Net.ServerSentEvents.dll.so => 76
	i64 u0x77f8a4acc2fdc449, ; 603: System.Security.Cryptography.Cng.dll => 123
	i64 u0x780bc73597a503a9, ; 604: lib-ms-Microsoft.Maui.Controls.resources.dll.so => 410
	i64 u0x782c5d8eb99ff201, ; 605: lib_Microsoft.VisualBasic.Core.dll.so => 2
	i64 u0x783606d1e53e7a1a, ; 606: th/Microsoft.Maui.Controls.resources.dll => 420
	i64 u0x7888c8518f32343b, ; 607: tr/Microsoft.CodeAnalysis.resources.dll => 351
	i64 u0x78a45e51311409b6, ; 608: Xamarin.AndroidX.Fragment.dll => 282
	i64 u0x78ed4ab8f9d800a1, ; 609: Xamarin.AndroidX.Lifecycle.ViewModel => 295
	i64 u0x7947f4efc8e9966c, ; 610: Microsoft.CodeAnalysis.Scripting => 193
	i64 u0x795754f83b38b3ad, ; 611: lib_Microsoft.CodeAnalysis.Scripting.dll.so => 193
	i64 u0x796d689afabf16c2, ; 612: pt-BR/Microsoft.CodeAnalysis.Scripting.resources => 388
	i64 u0x7996e32deaf72986, ; 613: Microsoft.CodeAnalysis.CSharp.dll => 191
	i64 u0x79c9f0820bc356a5, ; 614: DynamicData.dll => 181
	i64 u0x7a25bdb29108c6e7, ; 615: Microsoft.Extensions.Http => 217
	i64 u0x7a5207a7c82d30b4, ; 616: lib_Xamarin.JSpecify.dll.so => 334
	i64 u0x7a71889545dcdb00, ; 617: lib_Microsoft.AspNetCore.Components.WebView.dll.so => 187
	i64 u0x7a7e7eddf79c5d26, ; 618: lib_Xamarin.AndroidX.Lifecycle.ViewModel.dll.so => 295
	i64 u0x7a9a57d43b0845fa, ; 619: System.AppContext => 6
	i64 u0x7ad0f4f1e5d08183, ; 620: Xamarin.AndroidX.Collection.dll => 264
	i64 u0x7adb8da2ac89b647, ; 621: fi/Microsoft.Maui.Controls.resources.dll => 400
	i64 u0x7b13d9eaa944ade8, ; 622: Xamarin.AndroidX.DynamicAnimation.dll => 278
	i64 u0x7b150145c0a9058c, ; 623: Microsoft.Data.Sqlite => 194
	i64 u0x7b4927e421291c41, ; 624: Microsoft.IdentityModel.JsonWebTokens.dll => 226
	i64 u0x7bef86a4335c4870, ; 625: System.ComponentModel.TypeConverter => 17
	i64 u0x7c0820144cd34d6a, ; 626: sk/Microsoft.Maui.Controls.resources.dll => 418
	i64 u0x7c2a0bd1e0f988fc, ; 627: lib-de-Microsoft.Maui.Controls.resources.dll.so => 397
	i64 u0x7c41d387501568ba, ; 628: System.Net.WebClient.dll => 79
	i64 u0x7c482cd79bd24b13, ; 629: lib_Xamarin.AndroidX.ConstraintLayout.dll.so => 268
	i64 u0x7c4867f3cb880d2f, ; 630: Microsoft.AspNetCore.Metadata => 189
	i64 u0x7c60acf6404e96b6, ; 631: Xamarin.AndroidX.Navigation.Common.Android.dll => 302
	i64 u0x7cd2ec8eaf5241cd, ; 632: System.Security.dll => 133
	i64 u0x7cf9ae50dd350622, ; 633: Xamarin.Jetbrains.Annotations.dll => 333
	i64 u0x7d61f5eb85697e65, ; 634: Binance.Net => 178
	i64 u0x7d649b75d580bb42, ; 635: ms/Microsoft.Maui.Controls.resources.dll => 410
	i64 u0x7d8b5821548f89e7, ; 636: Microsoft.AspNetCore.Components.Forms => 185
	i64 u0x7d8ee2bdc8e3aad1, ; 637: System.Numerics.Vectors => 85
	i64 u0x7df5df8db8eaa6ac, ; 638: Microsoft.Extensions.Logging.Debug => 220
	i64 u0x7dfc3d6d9d8d7b70, ; 639: System.Collections => 12
	i64 u0x7e2e564fa2f76c65, ; 640: lib_System.Diagnostics.Tracing.dll.so => 34
	i64 u0x7e302e110e1e1346, ; 641: lib_System.Security.Claims.dll.so => 121
	i64 u0x7e4465b3f78ad8d0, ; 642: Xamarin.KotlinX.Serialization.Core.dll => 339
	i64 u0x7e571cad5915e6c3, ; 643: lib_Xamarin.AndroidX.Lifecycle.Process.dll.so => 290
	i64 u0x7e6ac99e4e8df72f, ; 644: System.IO.Hashing => 176
	i64 u0x7e6b1ca712437d7d, ; 645: Xamarin.AndroidX.Emoji2.ViewsHelper => 280
	i64 u0x7e88d9bbdca2967e, ; 646: AccessibleTrader.Plugins.Fred.dll => 432
	i64 u0x7e8cdd26d9e30e7d, ; 647: lib-pt-BR-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 388
	i64 u0x7e946809d6008ef2, ; 648: lib_System.ObjectModel.dll.so => 87
	i64 u0x7ea0272c1b4a9635, ; 649: lib_Xamarin.Android.Glide.dll.so => 249
	i64 u0x7ecc13347c8fd849, ; 650: lib_System.ComponentModel.dll.so => 18
	i64 u0x7f00ddd9b9ca5a13, ; 651: Xamarin.AndroidX.ViewPager.dll => 323
	i64 u0x7f8efcd9c2121403, ; 652: pl/Microsoft.CodeAnalysis.Scripting.resources => 387
	i64 u0x7f9351cd44b1273f, ; 653: Microsoft.Extensions.Configuration.Abstractions => 202
	i64 u0x7fbd557c99b3ce6f, ; 654: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.dll.so => 288
	i64 u0x8076a9a44a2ca331, ; 655: System.Net.Quic => 73
	i64 u0x80da183a87731838, ; 656: System.Reflection.Metadata => 97
	i64 u0x80ee53ea610b3f78, ; 657: zh-Hans/Microsoft.CodeAnalysis.CSharp.resources => 365
	i64 u0x80fa55b6d1b0be99, ; 658: SQLitePCLRaw.provider.e_sqlite3 => 246
	i64 u0x8101a73bd4533440, ; 659: Microsoft.AspNetCore.Components.Web => 186
	i64 u0x812c069d5cdecc17, ; 660: System.dll => 168
	i64 u0x813814e867226a33, ; 661: lib_Microsoft.CodeAnalysis.CSharp.Scripting.dll.so => 192
	i64 u0x81381be520a60adb, ; 662: Xamarin.AndroidX.Interpolator.dll => 284
	i64 u0x81657cec2b31e8aa, ; 663: System.Net => 84
	i64 u0x81ab745f6c0f5ce6, ; 664: zh-Hant/Microsoft.Maui.Controls.resources => 426
	i64 u0x8277f2be6b5ce05f, ; 665: Xamarin.AndroidX.AppCompat => 258
	i64 u0x828f06563b30bc50, ; 666: lib_Xamarin.AndroidX.CardView.dll.so => 263
	i64 u0x82b399cb01b531c4, ; 667: lib_System.Web.dll.so => 157
	i64 u0x82df8f5532a10c59, ; 668: lib_System.Drawing.dll.so => 36
	i64 u0x82f0b6e911d13535, ; 669: lib_System.Transactions.dll.so => 154
	i64 u0x82f6403342e12049, ; 670: uk/Microsoft.Maui.Controls.resources => 422
	i64 u0x83a7afd2c49adc86, ; 671: lib_Microsoft.IdentityModel.Abstractions.dll.so => 225
	i64 u0x83c14ba66c8e2b8c, ; 672: zh-Hans/Microsoft.Maui.Controls.resources => 425
	i64 u0x83de69860da6cbdd, ; 673: Microsoft.Extensions.FileProviders.Composite => 212
	i64 u0x846ce984efea52c7, ; 674: System.Threading.Tasks.Parallel.dll => 147
	i64 u0x84ae73148a4557d2, ; 675: lib_System.IO.Pipes.dll.so => 56
	i64 u0x84b01102c12a9232, ; 676: System.Runtime.Serialization.Json.dll => 115
	i64 u0x84cd5cdec0f54bcc, ; 677: lib_Microsoft.EntityFrameworkCore.Relational.dll.so => 197
	i64 u0x84f9060cc4a93c8f, ; 678: lib_SkiaSharp.dll.so => 239
	i64 u0x850c5ba0b57ce8e7, ; 679: lib_Xamarin.AndroidX.Collection.dll.so => 264
	i64 u0x851d02edd334b044, ; 680: Xamarin.AndroidX.VectorDrawable => 320
	i64 u0x85c919db62150978, ; 681: Xamarin.AndroidX.Transition.dll => 319
	i64 u0x863a7c3aa6592d25, ; 682: zh-Hans/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 378
	i64 u0x8662aaeb94fef37f, ; 683: lib_System.Dynamic.Runtime.dll.so => 37
	i64 u0x86a909228dc7657b, ; 684: lib-zh-Hant-Microsoft.Maui.Controls.resources.dll.so => 426
	i64 u0x86b3e00c36b84509, ; 685: Microsoft.Extensions.Configuration.dll => 201
	i64 u0x86b62cb077ec4fd7, ; 686: System.Runtime.Serialization.Xml => 117
	i64 u0x8704193f462e892e, ; 687: lib_Microsoft.Extensions.FileSystemGlobbing.dll.so => 215
	i64 u0x8706ffb12bf3f53d, ; 688: Xamarin.AndroidX.Annotation.Experimental => 256
	i64 u0x872a5b14c18d328c, ; 689: System.ComponentModel.DataAnnotations => 14
	i64 u0x872fb9615bc2dff0, ; 690: Xamarin.Android.Glide.Annotations.dll => 250
	i64 u0x87c4b8a492b176ad, ; 691: Microsoft.EntityFrameworkCore.Abstractions => 196
	i64 u0x87c69b87d9283884, ; 692: lib_System.Threading.Thread.dll.so => 149
	i64 u0x87f6569b25707834, ; 693: System.IO.Compression.Brotli.dll => 43
	i64 u0x8842b3a5d2d3fb36, ; 694: Microsoft.Maui.Essentials => 233
	i64 u0x88826e51a5d4a3d0, ; 695: de/Microsoft.CodeAnalysis.resources.dll => 342
	i64 u0x88926583efe7ee86, ; 696: Xamarin.AndroidX.Activity.Ktx.dll => 254
	i64 u0x88ba6bc4f7762b03, ; 697: lib_System.Reflection.dll.so => 100
	i64 u0x88bda98e0cffb7a9, ; 698: lib_Xamarin.KotlinX.Coroutines.Core.Jvm.dll.so => 338
	i64 u0x8930322c7bd8f768, ; 699: netstandard => 171
	i64 u0x897a606c9e39c75f, ; 700: lib_System.ComponentModel.Primitives.dll.so => 16
	i64 u0x898a5c6bc9e47ec1, ; 701: lib_Xamarin.AndroidX.SavedState.SavedState.Android.dll.so => 311
	i64 u0x89911a22005b92b7, ; 702: System.IO.FileSystem.DriveInfo.dll => 48
	i64 u0x89c5188089ec2cd5, ; 703: lib_System.Runtime.InteropServices.RuntimeInformation.dll.so => 109
	i64 u0x8a19e3dc71b34b2c, ; 704: System.Reflection.TypeExtensions.dll => 99
	i64 u0x8a399a706fcbce4b, ; 705: Microsoft.Extensions.Caching.Abstractions => 199
	i64 u0x8ad229ea26432ee2, ; 706: Xamarin.AndroidX.Loader => 300
	i64 u0x8b226e2ff894c966, ; 707: lib_CryptoExchange.Net.dll.so => 180
	i64 u0x8b4ff5d0fdd5faa1, ; 708: lib_System.Diagnostics.DiagnosticSource.dll.so => 27
	i64 u0x8b541d476eb3774c, ; 709: System.Security.Principal.Windows => 130
	i64 u0x8b8d01333a96d0b5, ; 710: System.Diagnostics.Process.dll => 29
	i64 u0x8b9ceca7acae3451, ; 711: lib-he-Microsoft.Maui.Controls.resources.dll.so => 402
	i64 u0x8c39b02ed181787b, ; 712: pt-BR/Microsoft.CodeAnalysis.CSharp.resources => 362
	i64 u0x8c575135aa1ccef4, ; 713: Microsoft.Extensions.FileProviders.Abstractions => 211
	i64 u0x8cb8f612b633affb, ; 714: Xamarin.AndroidX.SavedState.SavedState.Ktx.dll => 312
	i64 u0x8cdfdb4ce85fb925, ; 715: lib_System.Security.Principal.Windows.dll.so => 130
	i64 u0x8cdfe7b8f4caa426, ; 716: System.IO.Compression.FileSystem => 44
	i64 u0x8d0f420977c2c1c7, ; 717: Xamarin.AndroidX.CursorAdapter.dll => 274
	i64 u0x8d52a25632e81824, ; 718: Microsoft.EntityFrameworkCore.Sqlite.dll => 198
	i64 u0x8d52f7ea2796c531, ; 719: Xamarin.AndroidX.Emoji2.dll => 279
	i64 u0x8d7b8ab4b3310ead, ; 720: System.Threading => 152
	i64 u0x8da188285aadfe8e, ; 721: System.Collections.Concurrent => 8
	i64 u0x8e8f269ad1e1ff94, ; 722: lib_Xamarin.AndroidX.Tracing.Tracing.Android.dll.so => 318
	i64 u0x8ec6e06a61c1baeb, ; 723: lib_Newtonsoft.Json.dll.so => 235
	i64 u0x8ed807bfe9858dfc, ; 724: Xamarin.AndroidX.Navigation.Common => 301
	i64 u0x8ee08b8194a30f48, ; 725: lib-hi-Microsoft.Maui.Controls.resources.dll.so => 403
	i64 u0x8ef7601039857a44, ; 726: lib-ro-Microsoft.Maui.Controls.resources.dll.so => 416
	i64 u0x8ef9414937d93a0a, ; 727: SQLitePCLRaw.core.dll => 244
	i64 u0x8f32c6f611f6ffab, ; 728: pt/Microsoft.Maui.Controls.resources.dll => 415
	i64 u0x8f44b45eb046bbd1, ; 729: System.ServiceModel.Web.dll => 134
	i64 u0x8f8829d21c8985a4, ; 730: lib-pt-BR-Microsoft.Maui.Controls.resources.dll.so => 414
	i64 u0x8f8b0f07edd7b3b6, ; 731: cs/Microsoft.CodeAnalysis.resources.dll => 341
	i64 u0x8fa404e6277d0694, ; 732: zh-Hans/Microsoft.CodeAnalysis.CSharp.resources.dll => 365
	i64 u0x8fbf5b0114c6dcef, ; 733: System.Globalization.dll => 42
	i64 u0x8fcc8c2a81f3d9e7, ; 734: Xamarin.KotlinX.Serialization.Core => 339
	i64 u0x8fd27d934d7b3a55, ; 735: SQLitePCLRaw.core => 244
	i64 u0x90263f8448b8f572, ; 736: lib_System.Diagnostics.TraceSource.dll.so => 33
	i64 u0x903101b46fb73a04, ; 737: _Microsoft.Android.Resource.Designer => 435
	i64 u0x90393bd4865292f3, ; 738: lib_System.IO.Compression.dll.so => 46
	i64 u0x905e2b8e7ae91ae6, ; 739: System.Threading.Tasks.Extensions.dll => 146
	i64 u0x90634f86c5ebe2b5, ; 740: Xamarin.AndroidX.Lifecycle.ViewModel.Android => 296
	i64 u0x907b636704ad79ef, ; 741: lib_Microsoft.Maui.Controls.Xaml.dll.so => 231
	i64 u0x90b20db1fb5b1ab3, ; 742: ja/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 372
	i64 u0x90e9efbfd68593e0, ; 743: lib_Xamarin.AndroidX.Lifecycle.LiveData.dll.so => 287
	i64 u0x90f95fc914407a17, ; 744: lib-pl-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 361
	i64 u0x9130488ea254524d, ; 745: lib-de-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 381
	i64 u0x91418dc638b29e68, ; 746: lib_Xamarin.AndroidX.CustomView.dll.so => 275
	i64 u0x914647982e998267, ; 747: Microsoft.Extensions.Configuration.Json => 205
	i64 u0x9157bd523cd7ed36, ; 748: lib_System.Text.Json.dll.so => 140
	i64 u0x91a74f07b30d37e2, ; 749: System.Linq.dll => 63
	i64 u0x91cb86ea3b17111d, ; 750: System.ServiceModel.Web => 134
	i64 u0x91fa41a87223399f, ; 751: ca/Microsoft.Maui.Controls.resources.dll => 394
	i64 u0x92054e486c0c7ea7, ; 752: System.IO.FileSystem.DriveInfo => 48
	i64 u0x926c3cf189fe2e18, ; 753: zh-Hans/Microsoft.CodeAnalysis.resources.dll => 352
	i64 u0x928614058c40c4cd, ; 754: lib_System.Xml.XPath.XDocument.dll.so => 163
	i64 u0x929cc88e0bc1eddb, ; 755: cs/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 367
	i64 u0x92b138fffca2b01e, ; 756: lib_Xamarin.AndroidX.Arch.Core.Runtime.dll.so => 261
	i64 u0x92dfc2bfc6c6a888, ; 757: Xamarin.AndroidX.Lifecycle.LiveData => 287
	i64 u0x933da2c779423d68, ; 758: Xamarin.Android.Glide.Annotations => 250
	i64 u0x9388aad9b7ae40ce, ; 759: lib_Xamarin.AndroidX.Lifecycle.Common.dll.so => 285
	i64 u0x93ba953181e66fd2, ; 760: lib-ru-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 363
	i64 u0x93cfa73ab28d6e35, ; 761: ms/Microsoft.Maui.Controls.resources => 410
	i64 u0x941c00d21e5c0679, ; 762: lib_Xamarin.AndroidX.Transition.dll.so => 319
	i64 u0x944077d8ca3c6580, ; 763: System.IO.Compression.dll => 46
	i64 u0x948cffedc8ed7960, ; 764: System.Xml => 167
	i64 u0x948d746a7702861f, ; 765: Microsoft.IdentityModel.Logging.dll => 227
	i64 u0x94bbeab0d4764588, ; 766: System.IO.Hashing.dll => 176
	i64 u0x94c8990839c4bdb1, ; 767: lib_Xamarin.AndroidX.Interpolator.dll.so => 284
	i64 u0x9564283c37ed59a9, ; 768: lib_Microsoft.IdentityModel.Logging.dll.so => 227
	i64 u0x961e80b393a1fa82, ; 769: fr/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 370
	i64 u0x967fc325e09bfa8c, ; 770: es/Microsoft.Maui.Controls.resources => 399
	i64 u0x9686161486d34b81, ; 771: lib_Xamarin.AndroidX.ExifInterface.dll.so => 281
	i64 u0x9732d8dbddea3d9a, ; 772: id/Microsoft.Maui.Controls.resources => 406
	i64 u0x978be80e5210d31b, ; 773: Microsoft.Maui.Graphics.dll => 234
	i64 u0x97b8c771ea3e4220, ; 774: System.ComponentModel.dll => 18
	i64 u0x97e144c9d3c6976e, ; 775: System.Collections.Concurrent.dll => 8
	i64 u0x98270c46908e26f7, ; 776: zh-Hant/Microsoft.CodeAnalysis.CSharp.resources.dll => 366
	i64 u0x983fad367215b4f2, ; 777: ru/Microsoft.CodeAnalysis.Scripting.resources.dll => 389
	i64 u0x984184e3c70d4419, ; 778: GoogleGson => 182
	i64 u0x9843944103683dd3, ; 779: Xamarin.AndroidX.Core.Core.Ktx => 272
	i64 u0x98b05cc81e6f333c, ; 780: Xamarin.AndroidX.SavedState.SavedState.Android.dll => 311
	i64 u0x98d720cc4597562c, ; 781: System.Security.Cryptography.OpenSsl => 126
	i64 u0x98e6c4091164aa39, ; 782: lib-ko-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 373
	i64 u0x991d510397f92d9d, ; 783: System.Linq.Expressions => 60
	i64 u0x996ceeb8a3da3d67, ; 784: System.Threading.Overlapped.dll => 144
	i64 u0x999cb19e1a04ffd3, ; 785: CommunityToolkit.Mvvm.dll => 179
	i64 u0x99a00ca5270c6878, ; 786: Xamarin.AndroidX.Navigation.Runtime => 304
	i64 u0x99a891b860c3d03b, ; 787: lib-ko-Microsoft.CodeAnalysis.resources.dll.so => 347
	i64 u0x99cdc6d1f2d3a72f, ; 788: ko/Microsoft.Maui.Controls.resources.dll => 409
	i64 u0x9a01b1da98b6ee10, ; 789: Xamarin.AndroidX.Lifecycle.Runtime.dll => 291
	i64 u0x9a102e560c6efe86, ; 790: lib-pt-BR-Microsoft.CodeAnalysis.resources.dll.so => 349
	i64 u0x9a5ccc274fd6e6ee, ; 791: Jsr305Binding.dll => 329
	i64 u0x9ae6940b11c02876, ; 792: lib_Xamarin.AndroidX.Window.dll.so => 325
	i64 u0x9b211a749105beac, ; 793: System.Transactions.Local => 153
	i64 u0x9b8734714671022d, ; 794: System.Threading.Tasks.Dataflow.dll => 145
	i64 u0x9ba8c32873c681c1, ; 795: it/Microsoft.CodeAnalysis.CSharp.resources.dll => 358
	i64 u0x9bc6aea27fbf034f, ; 796: lib_Xamarin.KotlinX.Coroutines.Core.dll.so => 337
	i64 u0x9be4124ffc84e7ee, ; 797: pl/Microsoft.CodeAnalysis.resources.dll => 348
	i64 u0x9c23027a7e62afa2, ; 798: it/Microsoft.CodeAnalysis.Scripting.resources => 384
	i64 u0x9c244ac7cda32d26, ; 799: System.Security.Cryptography.X509Certificates.dll => 128
	i64 u0x9c465f280cf43733, ; 800: lib_Xamarin.KotlinX.Coroutines.Android.dll.so => 336
	i64 u0x9c69fdfa9a154b28, ; 801: tr/Microsoft.CodeAnalysis.CSharp.resources.dll => 364
	i64 u0x9c8f6872beab6408, ; 802: System.Xml.XPath.XDocument.dll => 163
	i64 u0x9ce01cf91101ae23, ; 803: System.Xml.XmlDocument => 165
	i64 u0x9cfa5a38cce8589d, ; 804: AccessibleTrader.Plugins.Fred => 432
	i64 u0x9d052eb79c53b587, ; 805: lib_Polly.dll.so => 236
	i64 u0x9d128180c81d7ce6, ; 806: Xamarin.AndroidX.CustomView.PoolingContainer => 276
	i64 u0x9d5dbcf5a48583fe, ; 807: lib_Xamarin.AndroidX.Activity.dll.so => 253
	i64 u0x9d74dee1a7725f34, ; 808: Microsoft.Extensions.Configuration.Abstractions.dll => 202
	i64 u0x9db5f1062fcf5cf3, ; 809: ru/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 376
	i64 u0x9dcb570d9792d506, ; 810: lib-ru-Microsoft.CodeAnalysis.resources.dll.so => 350
	i64 u0x9dd0e195825d65c6, ; 811: lib_Xamarin.AndroidX.Navigation.Runtime.Android.dll.so => 305
	i64 u0x9e4534b6adaf6e84, ; 812: nl/Microsoft.Maui.Controls.resources => 412
	i64 u0x9e4b95dec42769f7, ; 813: System.Diagnostics.Debug.dll => 26
	i64 u0x9e5a208afd9d15a6, ; 814: it/Microsoft.CodeAnalysis.CSharp.resources => 358
	i64 u0x9eaf1efdf6f7267e, ; 815: Xamarin.AndroidX.Navigation.Common.dll => 301
	i64 u0x9ef542cf1f78c506, ; 816: Xamarin.AndroidX.Lifecycle.LiveData.Core => 288
	i64 u0x9f4a6314df5b756a, ; 817: lib_AccessibleTrader.Plugins.Alpaca.dll.so => 428
	i64 u0x9f98edd7ed0d2e46, ; 818: ru/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 376
	i64 u0x9fbb2961ca18e5c2, ; 819: Microsoft.Extensions.FileProviders.Physical.dll => 214
	i64 u0xa00832eb975f56a8, ; 820: lib_System.Net.dll.so => 84
	i64 u0xa0ad78236b7b267f, ; 821: Xamarin.AndroidX.Window => 325
	i64 u0xa0d8259f4cc284ec, ; 822: lib_System.Security.Cryptography.dll.so => 129
	i64 u0xa0e17ca50c77a225, ; 823: lib_Xamarin.Google.Crypto.Tink.Android.dll.so => 330
	i64 u0xa0ff9b3e34d92f11, ; 824: lib_System.Resources.Writer.dll.so => 103
	i64 u0xa12fbfb4da97d9f3, ; 825: System.Threading.Timer.dll => 151
	i64 u0xa1440773ee9d341e, ; 826: Xamarin.Google.Android.Material => 328
	i64 u0xa18c39c44cdc3465, ; 827: Xamarin.AndroidX.Window.WindowCore => 326
	i64 u0xa1b9d7c27f47219f, ; 828: Xamarin.AndroidX.Navigation.UI.dll => 306
	i64 u0xa2572680829d2c7c, ; 829: System.IO.Pipelines.dll => 54
	i64 u0xa26597e57ee9c7f6, ; 830: System.Xml.XmlDocument.dll => 165
	i64 u0xa2beee74530fc01c, ; 831: SkiaSharp.Views.Android => 240
	i64 u0xa308401900e5bed3, ; 832: lib_mscorlib.dll.so => 170
	i64 u0xa34c544cec85bec6, ; 833: Microsoft.CodeAnalysis.CSharp.Scripting.dll => 192
	i64 u0xa395572e7da6c99d, ; 834: lib_System.Security.dll.so => 133
	i64 u0xa3b8104115a36bf6, ; 835: lib_Microsoft.Extensions.FileProviders.Embedded.dll.so => 213
	i64 u0xa3d089b150e18d27, ; 836: pt-BR/Microsoft.CodeAnalysis.resources.dll => 349
	i64 u0xa3e683f24b43af6f, ; 837: System.Dynamic.Runtime.dll => 37
	i64 u0xa4145becdee3dc4f, ; 838: Xamarin.AndroidX.VectorDrawable.Animated => 321
	i64 u0xa46aa1eaa214539b, ; 839: ko/Microsoft.Maui.Controls.resources => 409
	i64 u0xa4a372eecb9e4df0, ; 840: Microsoft.Extensions.Diagnostics => 209
	i64 u0xa4d20d2ff0563d26, ; 841: lib_CommunityToolkit.Mvvm.dll.so => 179
	i64 u0xa4e62983cf1e3674, ; 842: Microsoft.AspNetCore.Components.Forms.dll => 185
	i64 u0xa4edc8f2ceae241a, ; 843: System.Data.Common.dll => 22
	i64 u0xa5494f40f128ce6a, ; 844: System.Runtime.Serialization.Formatters.dll => 114
	i64 u0xa54b74df83dce92b, ; 845: System.Reflection.DispatchProxy => 92
	i64 u0xa5b7152421ed6d98, ; 846: lib_System.IO.FileSystem.Watcher.dll.so => 50
	i64 u0xa5c3844f17b822db, ; 847: lib_System.Linq.Parallel.dll.so => 61
	i64 u0xa5ce5c755bde8cb8, ; 848: lib_System.Security.Cryptography.Csp.dll.so => 124
	i64 u0xa5e599d1e0524750, ; 849: System.Numerics.Vectors.dll => 85
	i64 u0xa5f1ba49b85dd355, ; 850: System.Security.Cryptography.dll => 129
	i64 u0xa61975a5a37873ea, ; 851: lib_System.Xml.XmlSerializer.dll.so => 166
	i64 u0xa628f1af0ba9340a, ; 852: ko/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 373
	i64 u0xa6593e21584384d2, ; 853: lib_Jsr305Binding.dll.so => 329
	i64 u0xa66cbee0130865f7, ; 854: lib_WindowsBase.dll.so => 169
	i64 u0xa67dbee13e1df9ca, ; 855: Xamarin.AndroidX.SavedState.dll => 310
	i64 u0xa684b098dd27b296, ; 856: lib_Xamarin.AndroidX.Security.SecurityCrypto.dll.so => 313
	i64 u0xa68a420042bb9b1f, ; 857: Xamarin.AndroidX.DrawerLayout.dll => 277
	i64 u0xa6d26156d1cacc7c, ; 858: Xamarin.Android.Glide.dll => 249
	i64 u0xa75386b5cb9595aa, ; 859: Xamarin.AndroidX.Lifecycle.Runtime.Android => 292
	i64 u0xa763fbb98df8d9fb, ; 860: lib_Microsoft.Win32.Primitives.dll.so => 4
	i64 u0xa78ce3745383236a, ; 861: Xamarin.AndroidX.Lifecycle.Common.Jvm => 286
	i64 u0xa7c31b56b4dc7b33, ; 862: hu/Microsoft.Maui.Controls.resources => 405
	i64 u0xa7eab29ed44b4e7a, ; 863: Mono.Android.Export => 173
	i64 u0xa8195217cbf017b7, ; 864: Microsoft.VisualBasic.Core => 2
	i64 u0xa82fd211eef00a5b, ; 865: Microsoft.Extensions.FileProviders.Physical => 214
	i64 u0xa859a95830f367ff, ; 866: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll.so => 297
	i64 u0xa8adea9b1f260c23, ; 867: lib-it-Microsoft.CodeAnalysis.resources.dll.so => 345
	i64 u0xa8b52f21e0dbe690, ; 868: System.Runtime.Serialization.dll => 118
	i64 u0xa8be86a05ed1a53e, ; 869: lib_AccessibleTrader.Plugins.Fred.dll.so => 432
	i64 u0xa8e6320dd07580ef, ; 870: lib_Microsoft.IdentityModel.JsonWebTokens.dll.so => 226
	i64 u0xa8ee4ed7de2efaee, ; 871: Xamarin.AndroidX.Annotation.dll => 255
	i64 u0xa95590e7c57438a4, ; 872: System.Configuration => 19
	i64 u0xa9e0069186cb7a28, ; 873: Binance.Net.dll => 178
	i64 u0xaa2219c8e3449ff5, ; 874: Microsoft.Extensions.Logging.Abstractions => 219
	i64 u0xaa443ac34067eeef, ; 875: System.Private.Xml.dll => 91
	i64 u0xaa52de307ef5d1dd, ; 876: System.Net.Http => 66
	i64 u0xaa9a7b0214a5cc5c, ; 877: System.Diagnostics.StackTrace.dll => 30
	i64 u0xaaaf86367285a918, ; 878: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 207
	i64 u0xaae72bd80754669a, ; 879: lib-es-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 356
	i64 u0xaaf84bb3f052a265, ; 880: el/Microsoft.Maui.Controls.resources => 398
	i64 u0xab31ca393f222dc0, ; 881: Skender.Stock.Indicators => 238
	i64 u0xab9af77b5b67a0b8, ; 882: Xamarin.AndroidX.ConstraintLayout.Core => 269
	i64 u0xab9c1b2687d86b0b, ; 883: lib_System.Linq.Expressions.dll.so => 60
	i64 u0xac2af3fa195a15ce, ; 884: System.Runtime.Numerics => 113
	i64 u0xac5376a2a538dc10, ; 885: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 288
	i64 u0xac5acae88f60357e, ; 886: System.Diagnostics.Tools.dll => 32
	i64 u0xac79c7e46047ad98, ; 887: System.Security.Principal.Windows.dll => 130
	i64 u0xac98d31068e24591, ; 888: System.Xml.XDocument => 162
	i64 u0xacd46e002c3ccb97, ; 889: ro/Microsoft.Maui.Controls.resources => 416
	i64 u0xacdd9e4180d56dda, ; 890: Xamarin.AndroidX.Concurrent.Futures => 267
	i64 u0xacf42eea7ef9cd12, ; 891: System.Threading.Channels => 143
	i64 u0xad22fd2d199d997b, ; 892: zh-Hans/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 378
	i64 u0xad85b8f184f001b8, ; 893: AccessibleTrader.Plugins.Coinbase => 431
	i64 u0xad89c07347f1bad6, ; 894: nl/Microsoft.Maui.Controls.resources.dll => 412
	i64 u0xadbb53caf78a79d2, ; 895: System.Web.HttpUtility => 156
	i64 u0xadc90ab061a9e6e4, ; 896: System.ComponentModel.TypeConverter.dll => 17
	i64 u0xadca1b9030b9317e, ; 897: Xamarin.AndroidX.Collection.Ktx => 266
	i64 u0xadd8eda2edf396ad, ; 898: Xamarin.Android.Glide.GifDecoder => 252
	i64 u0xadf4cf30debbeb9a, ; 899: System.Net.ServicePoint.dll => 77
	i64 u0xadf511667bef3595, ; 900: System.Net.Security => 75
	i64 u0xae0aaa94fdcfce0f, ; 901: System.ComponentModel.EventBasedAsync.dll => 15
	i64 u0xae282bcd03739de7, ; 902: Java.Interop => 172
	i64 u0xae483506cdfe2b0f, ; 903: lib-cs-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 367
	i64 u0xae53579c90db1107, ; 904: System.ObjectModel.dll => 87
	i64 u0xaeafff290ccb288d, ; 905: cs/Microsoft.CodeAnalysis.CSharp.resources => 354
	i64 u0xaf12fb8133ac3fbb, ; 906: Microsoft.EntityFrameworkCore.Sqlite => 198
	i64 u0xaf732d0b2193b8f5, ; 907: System.Security.Cryptography.OpenSsl.dll => 126
	i64 u0xaf833d66e3dafcbd, ; 908: lib-it-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 371
	i64 u0xafdb94dbccd9d11c, ; 909: Xamarin.AndroidX.Lifecycle.LiveData.dll => 287
	i64 u0xafe29f45095518e7, ; 910: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll.so => 298
	i64 u0xb03ae931fb25607e, ; 911: Xamarin.AndroidX.ConstraintLayout => 268
	i64 u0xb05cc42cd94c6d9d, ; 912: lib-sv-Microsoft.Maui.Controls.resources.dll.so => 419
	i64 u0xb0ac21bec8f428c5, ; 913: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll => 294
	i64 u0xb0bb43dc52ea59f9, ; 914: System.Diagnostics.Tracing.dll => 34
	i64 u0xb0c6678edfb08a6d, ; 915: lib-es-Microsoft.CodeAnalysis.resources.dll.so => 343
	i64 u0xb1ccbf6243328d1c, ; 916: Microsoft.AspNetCore.Components => 184
	i64 u0xb1dd05401aa8ee63, ; 917: System.Security.AccessControl => 120
	i64 u0xb220631954820169, ; 918: System.Text.RegularExpressions => 141
	i64 u0xb2376e1dbf8b4ed7, ; 919: System.Security.Cryptography.Csp => 124
	i64 u0xb2483fcbee21f661, ; 920: tr/Microsoft.CodeAnalysis.Scripting.resources => 390
	i64 u0xb2a1959fe95c5402, ; 921: lib_System.Runtime.InteropServices.JavaScript.dll.so => 108
	i64 u0xb2a3f67f3bf29fce, ; 922: da/Microsoft.Maui.Controls.resources => 396
	i64 u0xb36f863ae8997ccc, ; 923: lib_AccessibleTrader.BlazorClient.dll.so => 0
	i64 u0xb3874072ee0ecf8c, ; 924: Xamarin.AndroidX.VectorDrawable.Animated.dll => 321
	i64 u0xb3d5b1cf730ea936, ; 925: pt-BR/Microsoft.CodeAnalysis.resources => 349
	i64 u0xb3f0a0fcda8d3ebc, ; 926: Xamarin.AndroidX.CardView => 263
	i64 u0xb40712daf22db873, ; 927: ko/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 373
	i64 u0xb46be1aa6d4fff93, ; 928: hi/Microsoft.Maui.Controls.resources => 403
	i64 u0xb477491be13109d8, ; 929: ar/Microsoft.Maui.Controls.resources => 393
	i64 u0xb49179af0e452f69, ; 930: zh-Hans/Microsoft.CodeAnalysis.Scripting.resources => 391
	i64 u0xb4b3092fd37a579a, ; 931: ja/Microsoft.CodeAnalysis.CSharp.resources.dll => 359
	i64 u0xb4bd7015ecee9d86, ; 932: System.IO.Pipelines => 54
	i64 u0xb4c53d9749c5f226, ; 933: lib_System.IO.FileSystem.AccessControl.dll.so => 47
	i64 u0xb4ff710863453fda, ; 934: System.Diagnostics.FileVersionInfo.dll => 28
	i64 u0xb54092076b15e062, ; 935: System.Threading.AccessControl => 142
	i64 u0xb5c38bf497a4cfe2, ; 936: lib_System.Threading.Tasks.dll.so => 148
	i64 u0xb5c7fcdafbc67ee4, ; 937: Microsoft.Extensions.Logging.Abstractions.dll => 219
	i64 u0xb5ea31d5244c6626, ; 938: System.Threading.ThreadPool.dll => 150
	i64 u0xb6daa312e893d3c4, ; 939: lib-ja-Microsoft.CodeAnalysis.resources.dll.so => 346
	i64 u0xb7212c4683a94afe, ; 940: System.Drawing.Primitives => 35
	i64 u0xb7b7753d1f319409, ; 941: sv/Microsoft.Maui.Controls.resources => 419
	i64 u0xb81a2c6e0aee50fe, ; 942: lib_System.Private.CoreLib.dll.so => 177
	i64 u0xb872c26142d22aa9, ; 943: Microsoft.Extensions.Http.dll => 217
	i64 u0xb8c60af47c08d4da, ; 944: System.Net.ServicePoint => 77
	i64 u0xb8e560c18b3fa538, ; 945: AccessibleTrader.Core => 427
	i64 u0xb8e68d20aad91196, ; 946: lib_System.Xml.XPath.dll.so => 164
	i64 u0xb9185c33a1643eed, ; 947: Microsoft.CSharp.dll => 1
	i64 u0xb960d6b2200ba320, ; 948: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll => 299
	i64 u0xb9b8001adf4ed7cc, ; 949: lib_Xamarin.AndroidX.SlidingPaneLayout.dll.so => 314
	i64 u0xb9f64d3b230def68, ; 950: lib-pt-Microsoft.Maui.Controls.resources.dll.so => 415
	i64 u0xb9fc3c8a556e3691, ; 951: ja/Microsoft.Maui.Controls.resources => 408
	i64 u0xba4670aa94a2b3c6, ; 952: lib_System.Xml.XDocument.dll.so => 162
	i64 u0xba48785529705af9, ; 953: System.Collections.dll => 12
	i64 u0xba965b8c86359996, ; 954: lib_System.Windows.dll.so => 158
	i64 u0xbaf762c4825c14e9, ; 955: Microsoft.AspNetCore.Components.WebView => 187
	i64 u0xbb286883bc35db36, ; 956: System.Transactions.dll => 154
	i64 u0xbb65706fde942ce3, ; 957: System.Net.Sockets => 78
	i64 u0xbb822a624c99bd72, ; 958: lib-zh-Hans-Microsoft.CodeAnalysis.resources.dll.so => 352
	i64 u0xbba28979413cad9e, ; 959: lib_System.Runtime.CompilerServices.VisualC.dll.so => 105
	i64 u0xbbd180354b67271a, ; 960: System.Runtime.Serialization.Formatters => 114
	i64 u0xbc0ad520c3be6d31, ; 961: ja/Microsoft.CodeAnalysis.resources => 346
	i64 u0xbc22a245dab70cb4, ; 962: lib_SQLitePCLRaw.provider.e_sqlite3.dll.so => 246
	i64 u0xbc260cdba33291a3, ; 963: Xamarin.AndroidX.Arch.Core.Common.dll => 260
	i64 u0xbc3c4e8dffea9d4e, ; 964: Microsoft.AspNetCore.Metadata.dll => 189
	i64 u0xbcd36316d29f27b4, ; 965: lib_Microsoft.AspNetCore.Authorization.dll.so => 183
	i64 u0xbd0e2c0d55246576, ; 966: System.Net.Http.dll => 66
	i64 u0xbd3fbd85b9e1cb29, ; 967: lib_System.Net.HttpListener.dll.so => 67
	i64 u0xbd437a2cdb333d0d, ; 968: Xamarin.AndroidX.ViewPager2 => 324
	i64 u0xbd4f572d2bd0a789, ; 969: System.IO.Compression.ZipFile.dll => 45
	i64 u0xbd5d0b88d3d647a5, ; 970: lib_Xamarin.AndroidX.Browser.dll.so => 262
	i64 u0xbd877b14d0b56392, ; 971: System.Runtime.Intrinsics.dll => 111
	i64 u0xbe65a49036345cf4, ; 972: lib_System.Buffers.dll.so => 7
	i64 u0xbee38d4a88835966, ; 973: Xamarin.AndroidX.AppCompat.AppCompatResources => 259
	i64 u0xbef9919db45b4ca7, ; 974: System.IO.Pipes.AccessControl => 55
	i64 u0xbf0fa68611139208, ; 975: lib_Xamarin.AndroidX.Annotation.dll.so => 255
	i64 u0xbfc1e1fb3095f2b3, ; 976: lib_System.Net.Http.Json.dll.so => 65
	i64 u0xbfd57e7eba42c6c7, ; 977: de/Microsoft.CodeAnalysis.CSharp.resources.dll => 355
	i64 u0xc040a4ab55817f58, ; 978: ar/Microsoft.Maui.Controls.resources.dll => 393
	i64 u0xc07cadab29efeba0, ; 979: Xamarin.AndroidX.Core.Core.Ktx.dll => 272
	i64 u0xc0d928351ab5ca77, ; 980: System.Console.dll => 20
	i64 u0xc0f5a221a9383aea, ; 981: System.Runtime.Intrinsics => 111
	i64 u0xc111030af54d7191, ; 982: System.Resources.Writer => 103
	i64 u0xc12b8b3afa48329c, ; 983: lib_System.Linq.dll.so => 63
	i64 u0xc183ca0b74453aa9, ; 984: lib_System.Threading.Tasks.Dataflow.dll.so => 145
	i64 u0xc1afcc0a4309f4e3, ; 985: ko/Microsoft.CodeAnalysis.resources.dll => 347
	i64 u0xc1c2cb7af77b8858, ; 986: Microsoft.EntityFrameworkCore => 195
	i64 u0xc1ebdc7e6a943450, ; 987: Microsoft.AspNetCore.Authorization.dll => 183
	i64 u0xc1ff9ae3cdb6e1e6, ; 988: Xamarin.AndroidX.Activity.dll => 253
	i64 u0xc26c064effb1dea9, ; 989: System.Buffers.dll => 7
	i64 u0xc278de356ad8a9e3, ; 990: Microsoft.IdentityModel.Logging => 227
	i64 u0xc28c50f32f81cc73, ; 991: ja/Microsoft.Maui.Controls.resources.dll => 408
	i64 u0xc2902f6cf5452577, ; 992: lib_Mono.Android.Export.dll.so => 173
	i64 u0xc2a3bca55b573141, ; 993: System.IO.FileSystem.Watcher => 50
	i64 u0xc2bcfec99f69365e, ; 994: Xamarin.AndroidX.ViewPager2.dll => 324
	i64 u0xc30b52815b58ac2c, ; 995: lib_System.Runtime.Serialization.Xml.dll.so => 117
	i64 u0xc3492f8f90f96ce4, ; 996: lib_Microsoft.Extensions.DependencyModel.dll.so => 208
	i64 u0xc36d7d89c652f455, ; 997: System.Threading.Overlapped => 144
	i64 u0xc396b285e59e5493, ; 998: GoogleGson.dll => 182
	i64 u0xc3c86c1e5e12f03d, ; 999: WindowsBase => 169
	i64 u0xc3e329d2e75fd52c, ; 1000: lib-ru-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 389
	i64 u0xc3e74964279d65e6, ; 1001: zh-Hans/Microsoft.CodeAnalysis.resources => 352
	i64 u0xc421b61fd853169d, ; 1002: lib_System.Net.WebSockets.Client.dll.so => 82
	i64 u0xc439b9764edec517, ; 1003: AccessibleTrader.BlazorClient.dll => 0
	i64 u0xc463e077917aa21d, ; 1004: System.Runtime.Serialization.Json => 115
	i64 u0xc472ce300460ccb6, ; 1005: Microsoft.EntityFrameworkCore.dll => 195
	i64 u0xc4d3858ed4d08512, ; 1006: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll => 298
	i64 u0xc4d69851fe06342f, ; 1007: lib_Microsoft.Extensions.Caching.Memory.dll.so => 200
	i64 u0xc50fded0ded1418c, ; 1008: lib_System.ComponentModel.TypeConverter.dll.so => 17
	i64 u0xc519125d6bc8fb11, ; 1009: lib_System.Net.Requests.dll.so => 74
	i64 u0xc5293b19e4dc230e, ; 1010: Xamarin.AndroidX.Navigation.Fragment => 303
	i64 u0xc5325b2fcb37446f, ; 1011: lib_System.Private.Xml.dll.so => 91
	i64 u0xc535cb9a21385d9b, ; 1012: lib_Xamarin.Android.Glide.DiskLruCache.dll.so => 251
	i64 u0xc5a0f4b95a699af7, ; 1013: lib_System.Private.Uri.dll.so => 89
	i64 u0xc5cdcd5b6277579e, ; 1014: lib_System.Security.Cryptography.Algorithms.dll.so => 122
	i64 u0xc5ec286825cb0bf4, ; 1015: Xamarin.AndroidX.Tracing.Tracing => 317
	i64 u0xc610594226ea2d3a, ; 1016: cs/Microsoft.CodeAnalysis.Scripting.resources => 380
	i64 u0xc659b586d4c229e2, ; 1017: Microsoft.Extensions.Configuration.FileExtensions.dll => 204
	i64 u0xc6706bc8aa7fe265, ; 1018: Xamarin.AndroidX.Annotation.Jvm => 257
	i64 u0xc6b898b1a52bd31f, ; 1019: lib_Binance.Net.dll.so => 178
	i64 u0xc74d70d4aa96cef3, ; 1020: Xamarin.AndroidX.Navigation.Runtime.Android => 305
	i64 u0xc7c01e7d7c93a110, ; 1021: System.Text.Encoding.Extensions.dll => 137
	i64 u0xc7ce851898a4548e, ; 1022: lib_System.Web.HttpUtility.dll.so => 156
	i64 u0xc809d4089d2556b2, ; 1023: System.Runtime.InteropServices.JavaScript.dll => 108
	i64 u0xc858a28d9ee5a6c5, ; 1024: lib_System.Collections.Specialized.dll.so => 11
	i64 u0xc8ac7c6bf1c2ec51, ; 1025: System.Reflection.DispatchProxy.dll => 92
	i64 u0xc90db8ddc202826f, ; 1026: AccessibleTrader.Plugins.Polygon.dll => 433
	i64 u0xc99f841cdb0c1d8d, ; 1027: AccessibleTrader.BlazorClient => 0
	i64 u0xc9b4f5dc9735cfdd, ; 1028: lib-zh-Hans-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 378
	i64 u0xc9c62c8f354ac568, ; 1029: lib_System.Diagnostics.TextWriterTraceListener.dll.so => 31
	i64 u0xca3110fea81c8916, ; 1030: Microsoft.AspNetCore.Components.Web.dll => 186
	i64 u0xca32340d8d54dcd5, ; 1031: Microsoft.Extensions.Caching.Memory.dll => 200
	i64 u0xca3a723e7342c5b6, ; 1032: lib-tr-Microsoft.Maui.Controls.resources.dll.so => 421
	i64 u0xca5801070d9fccfb, ; 1033: System.Text.Encoding => 138
	i64 u0xcab3493c70141c2d, ; 1034: pl/Microsoft.Maui.Controls.resources => 413
	i64 u0xcacfddc9f7c6de76, ; 1035: ro/Microsoft.Maui.Controls.resources.dll => 416
	i64 u0xcadbc92899a777f0, ; 1036: Xamarin.AndroidX.Startup.StartupRuntime => 315
	i64 u0xcb45618372c47127, ; 1037: Microsoft.EntityFrameworkCore.Relational => 197
	i64 u0xcb76efab0f56f81a, ; 1038: System.Reactive => 248
	i64 u0xcba1cb79f45292b5, ; 1039: Xamarin.Android.Glide.GifDecoder.dll => 252
	i64 u0xcbb5f80c7293e696, ; 1040: lib_System.Globalization.Calendars.dll.so => 40
	i64 u0xcbd4fdd9cef4a294, ; 1041: lib__Microsoft.Android.Resource.Designer.dll.so => 435
	i64 u0xcbf8c1d4c780610e, ; 1042: lib_Polly.Core.dll.so => 237
	i64 u0xcc15da1e07bbd994, ; 1043: Xamarin.AndroidX.SlidingPaneLayout => 314
	i64 u0xcc2876b32ef2794c, ; 1044: lib_System.Text.RegularExpressions.dll.so => 141
	i64 u0xcc5c3bb714c4561e, ; 1045: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 338
	i64 u0xcc76886e09b88260, ; 1046: Xamarin.KotlinX.Serialization.Core.Jvm.dll => 340
	i64 u0xcc9fa2923aa1c9ef, ; 1047: System.Diagnostics.Contracts.dll => 25
	i64 u0xccae9bb73e2326bd, ; 1048: lib_System.IO.Hashing.dll.so => 176
	i64 u0xccf25c4b634ccd3a, ; 1049: zh-Hans/Microsoft.Maui.Controls.resources.dll => 425
	i64 u0xcd10a42808629144, ; 1050: System.Net.Requests => 74
	i64 u0xcdca1b920e9f53ba, ; 1051: Xamarin.AndroidX.Interpolator => 284
	i64 u0xcdd0c48b6937b21c, ; 1052: Xamarin.AndroidX.SwipeRefreshLayout => 316
	i64 u0xcf23d8093f3ceadf, ; 1053: System.Diagnostics.DiagnosticSource.dll => 27
	i64 u0xcf5ff6b6b2c4c382, ; 1054: System.Net.Mail.dll => 68
	i64 u0xcf8fc898f98b0d34, ; 1055: System.Private.Xml.Linq => 90
	i64 u0xd013a947d55a77ab, ; 1056: lib-it-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 384
	i64 u0xd04b5f59ed596e31, ; 1057: System.Reflection.Metadata.dll => 97
	i64 u0xd063299fcfc0c93f, ; 1058: lib_System.Runtime.Serialization.Json.dll.so => 115
	i64 u0xd0973385d3eb8971, ; 1059: it/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 371
	i64 u0xd0de8a113e976700, ; 1060: System.Diagnostics.TextWriterTraceListener => 31
	i64 u0xd0fc33d5ae5d4cb8, ; 1061: System.Runtime.Extensions => 106
	i64 u0xd118cf03aa687fdf, ; 1062: cs/Microsoft.CodeAnalysis.resources => 341
	i64 u0xd1194e1d8a8de83c, ; 1063: lib_Xamarin.AndroidX.Lifecycle.Common.Jvm.dll.so => 286
	i64 u0xd12beacdfc14f696, ; 1064: System.Dynamic.Runtime => 37
	i64 u0xd16ec72089dd441b, ; 1065: CryptoExchange.Net => 180
	i64 u0xd16fd7fb9bbcd43e, ; 1066: Microsoft.Extensions.Diagnostics.Abstractions => 210
	i64 u0xd198e7ce1b6a8344, ; 1067: System.Net.Quic.dll => 73
	i64 u0xd2505d8abeed6983, ; 1068: lib_Microsoft.AspNetCore.Components.Web.dll.so => 186
	i64 u0xd2a56d8bff7b34be, ; 1069: es/Microsoft.CodeAnalysis.Scripting.resources.dll => 382
	i64 u0xd3144156a3727ebe, ; 1070: Xamarin.Google.Guava.ListenableFuture => 332
	i64 u0xd333d0af9e423810, ; 1071: System.Runtime.InteropServices => 110
	i64 u0xd33a415cb4278969, ; 1072: System.Security.Cryptography.Encoding.dll => 125
	i64 u0xd3426d966bb704f5, ; 1073: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 259
	i64 u0xd3651b6fc3125825, ; 1074: System.Private.Uri.dll => 89
	i64 u0xd373685349b1fe8b, ; 1075: Microsoft.Extensions.Logging.dll => 218
	i64 u0xd3801faafafb7698, ; 1076: System.Private.DataContractSerialization.dll => 88
	i64 u0xd3e4c8d6a2d5d470, ; 1077: it/Microsoft.Maui.Controls.resources => 407
	i64 u0xd3edcc1f25459a50, ; 1078: System.Reflection.Emit => 95
	i64 u0xd3f33084bf9149d8, ; 1079: cs/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 367
	i64 u0xd42655883bb8c19f, ; 1080: Microsoft.EntityFrameworkCore.Abstractions.dll => 196
	i64 u0xd4645626dffec99d, ; 1081: lib_Microsoft.Extensions.DependencyInjection.Abstractions.dll.so => 207
	i64 u0xd46b4a8758d1f3ee, ; 1082: Microsoft.Extensions.FileProviders.Composite.dll => 212
	i64 u0xd4fa0abb79079ea9, ; 1083: System.Security.Principal.dll => 131
	i64 u0xd5507e11a2b2839f, ; 1084: Xamarin.AndroidX.Lifecycle.ViewModelSavedState => 298
	i64 u0xd5c37cd77ea86c02, ; 1085: Microsoft.CodeAnalysis.CSharp.Scripting => 192
	i64 u0xd5d04bef8478ea19, ; 1086: Xamarin.AndroidX.Tracing.Tracing.dll => 317
	i64 u0xd60815f26a12e140, ; 1087: Microsoft.Extensions.Logging.Debug.dll => 220
	i64 u0xd6694f8359737e4e, ; 1088: Xamarin.AndroidX.SavedState => 310
	i64 u0xd6949e129339eae5, ; 1089: lib_Xamarin.AndroidX.Core.Core.Ktx.dll.so => 272
	i64 u0xd6d21782156bc35b, ; 1090: Xamarin.AndroidX.SwipeRefreshLayout.dll => 316
	i64 u0xd6de019f6af72435, ; 1091: Xamarin.AndroidX.ConstraintLayout.Core.dll => 269
	i64 u0xd6e2b34595283a1a, ; 1092: lib-es-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 382
	i64 u0xd70956d1e6deefb9, ; 1093: Jsr305Binding => 329
	i64 u0xd72329819cbbbc44, ; 1094: lib_Microsoft.Extensions.Configuration.Abstractions.dll.so => 202
	i64 u0xd72c760af136e863, ; 1095: System.Xml.XmlSerializer.dll => 166
	i64 u0xd753f071e44c2a03, ; 1096: lib_System.Security.SecureString.dll.so => 132
	i64 u0xd7b3764ada9d341d, ; 1097: lib_Microsoft.Extensions.Logging.Abstractions.dll.so => 219
	i64 u0xd7f0088bc5ad71f2, ; 1098: Xamarin.AndroidX.VersionedParcelable => 322
	i64 u0xd824ef6ab33f8f7a, ; 1099: Xamarin.AndroidX.Window.WindowCore.dll => 326
	i64 u0xd8fb25e28ae30a12, ; 1100: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll => 307
	i64 u0xda1dfa4c534a9251, ; 1101: Microsoft.Extensions.DependencyInjection => 206
	i64 u0xdad05a11827959a3, ; 1102: System.Collections.NonGeneric.dll => 10
	i64 u0xdaefdfe71aa53cf9, ; 1103: System.IO.FileSystem.Primitives => 49
	i64 u0xdb5383ab5865c007, ; 1104: lib-vi-Microsoft.Maui.Controls.resources.dll.so => 423
	i64 u0xdb58816721c02a59, ; 1105: lib_System.Reflection.Emit.ILGeneration.dll.so => 93
	i64 u0xdb8f858873e2186b, ; 1106: SkiaSharp.Views.Maui.Controls => 241
	i64 u0xdbeda89f832aa805, ; 1107: vi/Microsoft.Maui.Controls.resources.dll => 423
	i64 u0xdbf2a779fbc3ac31, ; 1108: System.Transactions.Local.dll => 153
	i64 u0xdbf9607a441b4505, ; 1109: System.Linq => 63
	i64 u0xdbfc90157a0de9b0, ; 1110: lib_System.Text.Encoding.dll.so => 138
	i64 u0xdc75032002d1a212, ; 1111: lib_System.Transactions.Local.dll.so => 153
	i64 u0xdca8be7403f92d4f, ; 1112: lib_System.Linq.Queryable.dll.so => 62
	i64 u0xdcbf1e32b739302e, ; 1113: de/Microsoft.CodeAnalysis.resources => 342
	i64 u0xdccc74da2720e84e, ; 1114: lib-ja-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 372
	i64 u0xdce2c53525640bf3, ; 1115: Microsoft.Extensions.Logging => 218
	i64 u0xdcf0ce03827b19b2, ; 1116: lib_AccessibleTrader.Plugins.Binance.dll.so => 429
	i64 u0xdcfac8ea6983b7d2, ; 1117: lib_DynamicData.dll.so => 181
	i64 u0xdd14049e4243731e, ; 1118: lib-it-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 358
	i64 u0xdd2b722d78ef5f43, ; 1119: System.Runtime.dll => 119
	i64 u0xdd67031857c72f96, ; 1120: lib_System.Text.Encodings.Web.dll.so => 139
	i64 u0xdd70765ad6162057, ; 1121: Xamarin.JSpecify => 334
	i64 u0xdd92e229ad292030, ; 1122: System.Numerics.dll => 86
	i64 u0xdde30e6b77aa6f6c, ; 1123: lib-zh-Hans-Microsoft.Maui.Controls.resources.dll.so => 425
	i64 u0xde110ae80fa7c2e2, ; 1124: System.Xml.XDocument.dll => 162
	i64 u0xde4726fcdf63a198, ; 1125: Xamarin.AndroidX.Transition => 319
	i64 u0xde572c2b2fb32f93, ; 1126: lib_System.Threading.Tasks.Extensions.dll.so => 146
	i64 u0xde8769ebda7d8647, ; 1127: hr/Microsoft.Maui.Controls.resources.dll => 404
	i64 u0xdee075f3477ef6be, ; 1128: Xamarin.AndroidX.ExifInterface.dll => 281
	i64 u0xdef7031c225c4fa7, ; 1129: lib_AccessibleTrader.Core.dll.so => 427
	i64 u0xdf4b773de8fb1540, ; 1130: System.Net.dll => 84
	i64 u0xdf9c7682560a9629, ; 1131: System.Net.ServerSentEvents => 76
	i64 u0xdfa254ebb4346068, ; 1132: System.Net.Ping => 71
	i64 u0xe0142572c095a480, ; 1133: Xamarin.AndroidX.AppCompat.dll => 258
	i64 u0xe021eaa401792a05, ; 1134: System.Text.Encoding.dll => 138
	i64 u0xe02f89350ec78051, ; 1135: Xamarin.AndroidX.CoordinatorLayout.dll => 270
	i64 u0xe0496b9d65ef5474, ; 1136: Xamarin.Android.Glide.DiskLruCache.dll => 251
	i64 u0xe0c6976bbf521a2b, ; 1137: AccessibleTrader.Plugins.Alpaca.dll => 428
	i64 u0xe10b760bb1462e7a, ; 1138: lib_System.Security.Cryptography.Primitives.dll.so => 127
	i64 u0xe192a588d4410686, ; 1139: lib_System.IO.Pipelines.dll.so => 54
	i64 u0xe1a08bd3fa539e0d, ; 1140: System.Runtime.Loader => 112
	i64 u0xe1a77eb8831f7741, ; 1141: System.Security.SecureString.dll => 132
	i64 u0xe1b52f9f816c70ef, ; 1142: System.Private.Xml.Linq.dll => 90
	i64 u0xe1e199c8ab02e356, ; 1143: System.Data.DataSetExtensions.dll => 23
	i64 u0xe1e852de9692e4b8, ; 1144: es/Microsoft.CodeAnalysis.CSharp.resources => 356
	i64 u0xe1ecfdb7fff86067, ; 1145: System.Net.Security.dll => 75
	i64 u0xe2252a80fe853de4, ; 1146: lib_System.Security.Principal.dll.so => 131
	i64 u0xe22fa4c9c645db62, ; 1147: System.Diagnostics.TextWriterTraceListener.dll => 31
	i64 u0xe24095a7afddaab3, ; 1148: lib_Microsoft.Extensions.Hosting.Abstractions.dll.so => 216
	i64 u0xe2420585aeceb728, ; 1149: System.Net.Requests.dll => 74
	i64 u0xe26692647e6bcb62, ; 1150: Xamarin.AndroidX.Lifecycle.Runtime.Ktx => 293
	i64 u0xe29b73bc11392966, ; 1151: lib-id-Microsoft.Maui.Controls.resources.dll.so => 406
	i64 u0xe2ad448dee50fbdf, ; 1152: System.Xml.Serialization => 161
	i64 u0xe2d920f978f5d85c, ; 1153: System.Data.DataSetExtensions => 23
	i64 u0xe2e426c7714fa0bc, ; 1154: Microsoft.Win32.Primitives.dll => 4
	i64 u0xe31089e70e4e84ee, ; 1155: Microsoft.AspNetCore.Components.WebView.Maui => 188
	i64 u0xe332bacb3eb4a806, ; 1156: Mono.Android.Export.dll => 173
	i64 u0xe3811d68d4fe8463, ; 1157: pt-BR/Microsoft.Maui.Controls.resources.dll => 414
	i64 u0xe3b7cbae5ad66c75, ; 1158: lib_System.Security.Cryptography.Encoding.dll.so => 125
	i64 u0xe3e8c409bce3f768, ; 1159: AccessibleTrader.Plugins.Binance.dll => 429
	i64 u0xe4292b48f3224d5b, ; 1160: lib_Xamarin.AndroidX.Core.ViewTree.dll.so => 273
	i64 u0xe494f7ced4ecd10a, ; 1161: hu/Microsoft.Maui.Controls.resources.dll => 405
	i64 u0xe4a9b1e40d1e8917, ; 1162: lib-fi-Microsoft.Maui.Controls.resources.dll.so => 400
	i64 u0xe4f74a0b5bf9703f, ; 1163: System.Runtime.Serialization.Primitives => 116
	i64 u0xe51aadb833ed7eb1, ; 1164: lib_Microsoft.CodeAnalysis.CSharp.dll.so => 191
	i64 u0xe529964b351f8a52, ; 1165: pt-BR/Microsoft.CodeAnalysis.CSharp.resources.dll => 362
	i64 u0xe5434e8a119ceb69, ; 1166: lib_Mono.Android.dll.so => 175
	i64 u0xe55703b9ce5c038a, ; 1167: System.Diagnostics.Tools => 32
	i64 u0xe57013c8afc270b5, ; 1168: Microsoft.VisualBasic => 3
	i64 u0xe5a1ab00af921efe, ; 1169: lib_AccessibleTrader.Sdk.dll.so => 434
	i64 u0xe62913cc36bc07ec, ; 1170: System.Xml.dll => 167
	i64 u0xe6e66793cecd1487, ; 1171: lib_AccessibleTrader.Plugins.Polygon.dll.so => 433
	i64 u0xe7b916eaefda3b00, ; 1172: fr/Microsoft.CodeAnalysis.resources.dll => 344
	i64 u0xe7bea09c4900a191, ; 1173: Xamarin.AndroidX.VectorDrawable.dll => 320
	i64 u0xe7dd1e7ea292e8bc, ; 1174: ko/Microsoft.CodeAnalysis.resources => 347
	i64 u0xe7e03cc18dcdeb49, ; 1175: lib_System.Diagnostics.StackTrace.dll.so => 30
	i64 u0xe7e147ff99a7a380, ; 1176: lib_System.Configuration.dll.so => 19
	i64 u0xe8397cf3948e7cb7, ; 1177: lib_Microsoft.Extensions.Options.ConfigurationExtensions.dll.so => 222
	i64 u0xe865c771a4011ece, ; 1178: de/Microsoft.CodeAnalysis.Scripting.resources => 381
	i64 u0xe86b0df4ba9e5db8, ; 1179: lib_Xamarin.AndroidX.Lifecycle.Runtime.Android.dll.so => 292
	i64 u0xe896622fe0902957, ; 1180: System.Reflection.Emit.dll => 95
	i64 u0xe89a2a9ef110899b, ; 1181: System.Drawing.dll => 36
	i64 u0xe8c5f8c100b5934b, ; 1182: Microsoft.Win32.Registry => 5
	i64 u0xe9772100456fb4b4, ; 1183: Microsoft.AspNetCore.Components.dll => 184
	i64 u0xe98163eb702ae5c5, ; 1184: Xamarin.AndroidX.Arch.Core.Runtime => 261
	i64 u0xe994f23ba4c143e5, ; 1185: Xamarin.KotlinX.Coroutines.Android => 336
	i64 u0xe9b9c8c0458fd92a, ; 1186: System.Windows => 158
	i64 u0xe9d166d87a7f2bdb, ; 1187: lib_Xamarin.AndroidX.Startup.StartupRuntime.dll.so => 315
	i64 u0xea154e342c6ac70f, ; 1188: Microsoft.Extensions.FileProviders.Embedded.dll => 213
	i64 u0xea5a4efc2ad81d1b, ; 1189: Xamarin.Google.ErrorProne.Annotations => 331
	i64 u0xea9da4f091bb0d3f, ; 1190: lib-tr-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 377
	i64 u0xeb2313fe9d65b785, ; 1191: Xamarin.AndroidX.ConstraintLayout.dll => 268
	i64 u0xeb322ae59a10b855, ; 1192: AccessibleTrader.Sdk => 434
	i64 u0xeb692e0662456c7c, ; 1193: lib_AccessibleTrader.Plugins.Coinbase.dll.so => 431
	i64 u0xec8abb68d340aac6, ; 1194: Microsoft.AspNetCore.Authorization => 183
	i64 u0xecd614b0c28f4cf0, ; 1195: pl/Microsoft.CodeAnalysis.Scripting.resources.dll => 387
	i64 u0xed19c616b3fcb7eb, ; 1196: Xamarin.AndroidX.VersionedParcelable.dll => 322
	i64 u0xedc4817167106c23, ; 1197: System.Net.Sockets.dll => 78
	i64 u0xedc632067fb20ff3, ; 1198: System.Memory.dll => 64
	i64 u0xedc8e4ca71a02a8b, ; 1199: Xamarin.AndroidX.Navigation.Runtime.dll => 304
	i64 u0xee4c2ef307cf87b1, ; 1200: cs/Microsoft.CodeAnalysis.Scripting.resources.dll => 380
	i64 u0xee81f5b3f1c4f83b, ; 1201: System.Threading.ThreadPool => 150
	i64 u0xeeb7ebb80150501b, ; 1202: lib_Xamarin.AndroidX.Collection.Jvm.dll.so => 265
	i64 u0xeed0f6520e39d6af, ; 1203: pt-BR/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 375
	i64 u0xeefc635595ef57f0, ; 1204: System.Security.Cryptography.Cng => 123
	i64 u0xef03b1b5a04e9709, ; 1205: System.Text.Encoding.CodePages.dll => 136
	i64 u0xef5bcbe61622ee5f, ; 1206: Xamarin.AndroidX.Tracing.Tracing.Android.dll => 318
	i64 u0xef602c523fe2e87a, ; 1207: lib_Xamarin.Google.Guava.ListenableFuture.dll.so => 332
	i64 u0xef72742e1bcca27a, ; 1208: Microsoft.Maui.Essentials.dll => 233
	i64 u0xefd1e0c4e5c9b371, ; 1209: System.Resources.ResourceManager.dll => 102
	i64 u0xefe8f8d5ed3c72ea, ; 1210: System.Formats.Tar.dll => 39
	i64 u0xefec0b7fdc57ec42, ; 1211: Xamarin.AndroidX.Activity => 253
	i64 u0xeff59cbde4363ec3, ; 1212: System.Threading.AccessControl.dll => 142
	i64 u0xf00c29406ea45e19, ; 1213: es/Microsoft.Maui.Controls.resources.dll => 399
	i64 u0xf09e47b6ae914f6e, ; 1214: System.Net.NameResolution => 69
	i64 u0xf0ac2b489fed2e35, ; 1215: lib_System.Diagnostics.Debug.dll.so => 26
	i64 u0xf0b176644a6e46bd, ; 1216: fr/Microsoft.CodeAnalysis.Scripting.resources.dll => 383
	i64 u0xf0bb49dadd3a1fe1, ; 1217: lib_System.Net.ServicePoint.dll.so => 77
	i64 u0xf0c16dff90fbf5d6, ; 1218: Xamarin.AndroidX.Window.WindowCore.Jvm => 327
	i64 u0xf0de2537ee19c6ca, ; 1219: lib_System.Net.WebHeaderCollection.dll.so => 80
	i64 u0xf1138779fa181c68, ; 1220: lib_Xamarin.AndroidX.Lifecycle.Runtime.dll.so => 291
	i64 u0xf11b621fc87b983f, ; 1221: Microsoft.Maui.Controls.Xaml.dll => 231
	i64 u0xf161f4f3c3b7e62c, ; 1222: System.Data => 24
	i64 u0xf16eb650d5a464bc, ; 1223: System.ValueTuple => 155
	i64 u0xf1c4b4005493d871, ; 1224: System.Formats.Asn1.dll => 38
	i64 u0xf22514cfad2d598b, ; 1225: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll.so => 299
	i64 u0xf238bd79489d3a96, ; 1226: lib-nl-Microsoft.Maui.Controls.resources.dll.so => 412
	i64 u0xf27ac96c4a2c11ce, ; 1227: lib-fr-Microsoft.CodeAnalysis.resources.dll.so => 344
	i64 u0xf2feea356ba760af, ; 1228: Xamarin.AndroidX.Arch.Core.Runtime.dll => 261
	i64 u0xf300e085f8acd238, ; 1229: lib_System.ServiceProcess.dll.so => 135
	i64 u0xf34e52b26e7e059d, ; 1230: System.Runtime.CompilerServices.VisualC.dll => 105
	i64 u0xf37221fda4ef8830, ; 1231: lib_Xamarin.Google.Android.Material.dll.so => 328
	i64 u0xf3ad9b8fb3eefd12, ; 1232: lib_System.IO.UnmanagedMemoryStream.dll.so => 57
	i64 u0xf3ddfe05336abf29, ; 1233: System => 168
	i64 u0xf408654b2a135055, ; 1234: System.Reflection.Emit.ILGeneration.dll => 93
	i64 u0xf4103170a1de5bd0, ; 1235: System.Linq.Queryable.dll => 62
	i64 u0xf41b241c82f75cde, ; 1236: ru/Microsoft.CodeAnalysis.CSharp.resources.dll => 363
	i64 u0xf41eebf9fb91e2a1, ; 1237: it/Microsoft.CodeAnalysis.resources.dll => 345
	i64 u0xf42b5c4d56f6a043, ; 1238: tr/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 377
	i64 u0xf42d20c23173d77c, ; 1239: lib_System.ServiceModel.Web.dll.so => 134
	i64 u0xf4727d423e5d26f3, ; 1240: SkiaSharp => 239
	i64 u0xf4c1dd70a5496a17, ; 1241: System.IO.Compression => 46
	i64 u0xf4d5dd0c159319cd, ; 1242: pl/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 374
	i64 u0xf4ecf4b9afc64781, ; 1243: System.ServiceProcess.dll => 135
	i64 u0xf4eeeaa566e9b970, ; 1244: lib_Xamarin.AndroidX.CustomView.PoolingContainer.dll.so => 276
	i64 u0xf518f63ead11fcd1, ; 1245: System.Threading.Tasks => 148
	i64 u0xf5967aac376787d7, ; 1246: Microsoft.CodeAnalysis.dll => 190
	i64 u0xf5fc7602fe27b333, ; 1247: System.Net.WebHeaderCollection => 80
	i64 u0xf6077741019d7428, ; 1248: Xamarin.AndroidX.CoordinatorLayout => 270
	i64 u0xf61ade9836ad4692, ; 1249: Microsoft.IdentityModel.Tokens.dll => 228
	i64 u0xf6742cbf457c450b, ; 1250: Xamarin.AndroidX.Lifecycle.Runtime.Android.dll => 292
	i64 u0xf6c0e7d55a7a4e4f, ; 1251: Microsoft.IdentityModel.JsonWebTokens => 226
	i64 u0xf6de7fa3776f8927, ; 1252: lib_Microsoft.Extensions.Configuration.Json.dll.so => 205
	i64 u0xf6e8de2aebcbb422, ; 1253: lib_Xamarin.AndroidX.Window.WindowCore.Jvm.dll.so => 327
	i64 u0xf6f893f692f8cb43, ; 1254: Microsoft.Extensions.Options.ConfigurationExtensions.dll => 222
	i64 u0xf70c0a7bf8ccf5af, ; 1255: System.Web => 157
	i64 u0xf77b20923f07c667, ; 1256: de/Microsoft.Maui.Controls.resources.dll => 397
	i64 u0xf785c93afc3b7acc, ; 1257: lib-ko-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 386
	i64 u0xf7e2cac4c45067b3, ; 1258: lib_System.Numerics.Vectors.dll.so => 85
	i64 u0xf7e74930e0e3d214, ; 1259: zh-HK/Microsoft.Maui.Controls.resources.dll => 424
	i64 u0xf7fa0bf77fe677cc, ; 1260: Newtonsoft.Json.dll => 235
	i64 u0xf84773b5c81e3cef, ; 1261: lib-uk-Microsoft.Maui.Controls.resources.dll.so => 422
	i64 u0xf8aac5ea82de1348, ; 1262: System.Linq.Queryable => 62
	i64 u0xf8b77539b362d3ba, ; 1263: lib_System.Reflection.Primitives.dll.so => 98
	i64 u0xf8e045dc345b2ea3, ; 1264: lib_Xamarin.AndroidX.RecyclerView.dll.so => 308
	i64 u0xf915dc29808193a1, ; 1265: System.Web.HttpUtility.dll => 156
	i64 u0xf96c777a2a0686f4, ; 1266: hi/Microsoft.Maui.Controls.resources.dll => 403
	i64 u0xf9be54c8bcf8ff3b, ; 1267: System.Security.AccessControl.dll => 120
	i64 u0xf9d1b36df657e81b, ; 1268: AccessibleTrader.Core.dll => 427
	i64 u0xf9eec5bb3a6aedc6, ; 1269: Microsoft.Extensions.Options => 221
	i64 u0xfa0e82300e67f913, ; 1270: lib_System.AppContext.dll.so => 6
	i64 u0xfa2fdb27e8a2c8e8, ; 1271: System.ComponentModel.EventBasedAsync => 15
	i64 u0xfa3f278f288b0e84, ; 1272: lib_System.Net.Security.dll.so => 75
	i64 u0xfa504dfa0f097d72, ; 1273: Microsoft.Extensions.FileProviders.Abstractions.dll => 211
	i64 u0xfa5ed7226d978949, ; 1274: lib-ar-Microsoft.Maui.Controls.resources.dll.so => 393
	i64 u0xfa5eec7bc90bf0a5, ; 1275: de/Microsoft.CodeAnalysis.Scripting.resources.dll => 381
	i64 u0xfa645d91e9fc4cba, ; 1276: System.Threading.Thread => 149
	i64 u0xfa99d44ebf9bea5b, ; 1277: SkiaSharp.Views.Maui.Core => 242
	i64 u0xfad4d2c770e827f9, ; 1278: lib_System.IO.IsolatedStorage.dll.so => 52
	i64 u0xfad7aabd979f6593, ; 1279: Skender.Stock.Indicators.dll => 238
	i64 u0xfb022853d73b7fa5, ; 1280: lib_SQLitePCLRaw.batteries_v2.dll.so => 243
	i64 u0xfb06dd2338e6f7c4, ; 1281: System.Net.Ping.dll => 71
	i64 u0xfb087abe5365e3b7, ; 1282: lib_System.Data.DataSetExtensions.dll.so => 23
	i64 u0xfb846e949baff5ea, ; 1283: System.Xml.Serialization.dll => 161
	i64 u0xfbad3e4ce4b98145, ; 1284: System.Security.Cryptography.X509Certificates => 128
	i64 u0xfbf0a31c9fc34bc4, ; 1285: lib_System.Net.Http.dll.so => 66
	i64 u0xfc6b7527cc280b3f, ; 1286: lib_System.Runtime.Serialization.Formatters.dll.so => 114
	i64 u0xfc719aec26adf9d9, ; 1287: Xamarin.AndroidX.Navigation.Fragment.dll => 303
	i64 u0xfc82690c2fe2735c, ; 1288: Xamarin.AndroidX.Lifecycle.Process.dll => 290
	i64 u0xfc93fc307d279893, ; 1289: System.IO.Pipes.AccessControl.dll => 55
	i64 u0xfcd302092ada6328, ; 1290: System.IO.MemoryMappedFiles.dll => 53
	i64 u0xfd22f00870e40ae0, ; 1291: lib_Xamarin.AndroidX.DrawerLayout.dll.so => 277
	i64 u0xfd2e866c678cac90, ; 1292: lib_Microsoft.AspNetCore.Components.WebView.Maui.dll.so => 188
	i64 u0xfd49b3c1a76e2748, ; 1293: System.Runtime.InteropServices.RuntimeInformation => 109
	i64 u0xfd536c702f64dc47, ; 1294: System.Text.Encoding.Extensions => 137
	i64 u0xfd583f7657b6a1cb, ; 1295: Xamarin.AndroidX.Fragment => 282
	i64 u0xfd8dd91a2c26bd5d, ; 1296: Xamarin.AndroidX.Lifecycle.Runtime => 291
	i64 u0xfda36abccf05cf5c, ; 1297: System.Net.WebSockets.Client => 82
	i64 u0xfddbe9695626a7f5, ; 1298: Xamarin.AndroidX.Lifecycle.Common => 285
	i64 u0xfe9856c3af9365ab, ; 1299: lib_Microsoft.Extensions.Configuration.FileExtensions.dll.so => 204
	i64 u0xfeae9952cf03b8cb, ; 1300: tr/Microsoft.Maui.Controls.resources => 421
	i64 u0xfebe1950717515f9, ; 1301: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll => 289
	i64 u0xfec8e01187d0178c, ; 1302: lib-ja-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 359
	i64 u0xfec9a8625385376c, ; 1303: pt-BR/Microsoft.CodeAnalysis.Scripting.resources.dll => 388
	i64 u0xff270a55858bac8d, ; 1304: System.Security.Principal => 131
	i64 u0xff9b54613e0d2cc8, ; 1305: System.Net.Http.Json => 65
	i64 u0xffcfc81f609b0813, ; 1306: zh-Hant/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 379
	i64 u0xffdb7a971be4ec73 ; 1307: System.ValueTuple.dll => 155
], align 8

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [1308 x i32] [
	i32 42, i32 337, i32 380, i32 316, i32 13, i32 304, i32 222, i32 107,
	i32 200, i32 174, i32 48, i32 430, i32 258, i32 7, i32 246, i32 88,
	i32 417, i32 395, i32 423, i32 225, i32 278, i32 72, i32 308, i32 194,
	i32 12, i32 232, i32 104, i32 194, i32 424, i32 159, i32 19, i32 283,
	i32 265, i32 184, i32 164, i32 280, i32 320, i32 170, i32 417, i32 10,
	i32 220, i32 321, i32 98, i32 276, i32 277, i32 13, i32 221, i32 10,
	i32 368, i32 129, i32 97, i32 199, i32 143, i32 39, i32 418, i32 340,
	i32 323, i32 414, i32 175, i32 252, i32 5, i32 233, i32 68, i32 313,
	i32 353, i32 132, i32 312, i32 238, i32 279, i32 69, i32 345, i32 240,
	i32 342, i32 266, i32 67, i32 57, i32 275, i32 190, i32 52, i32 190,
	i32 368, i32 43, i32 127, i32 363, i32 392, i32 68, i32 83, i32 293,
	i32 360, i32 161, i32 94, i32 101, i32 308, i32 372, i32 144, i32 355,
	i32 154, i32 262, i32 353, i32 389, i32 401, i32 165, i32 172, i32 402,
	i32 207, i32 360, i32 83, i32 334, i32 266, i32 364, i32 4, i32 392,
	i32 5, i32 51, i32 103, i32 208, i32 56, i32 122, i32 100, i32 171,
	i32 120, i32 337, i32 21, i32 405, i32 390, i32 139, i32 237, i32 99,
	i32 340, i32 354, i32 79, i32 411, i32 353, i32 315, i32 121, i32 346,
	i32 8, i32 356, i32 168, i32 420, i32 71, i32 251, i32 185, i32 294,
	i32 309, i32 348, i32 209, i32 214, i32 174, i32 148, i32 40, i32 370,
	i32 313, i32 47, i32 30, i32 306, i32 409, i32 147, i32 221, i32 166,
	i32 28, i32 374, i32 86, i32 317, i32 387, i32 79, i32 43, i32 29,
	i32 224, i32 42, i32 105, i32 119, i32 368, i32 256, i32 45, i32 93,
	i32 420, i32 56, i32 151, i32 149, i32 195, i32 102, i32 49, i32 20,
	i32 271, i32 350, i32 116, i32 249, i32 401, i32 330, i32 369, i32 243,
	i32 371, i32 335, i32 223, i32 96, i32 58, i32 247, i32 351, i32 406,
	i32 404, i32 224, i32 326, i32 83, i32 330, i32 172, i32 26, i32 72,
	i32 307, i32 210, i32 239, i32 281, i32 302, i32 422, i32 369, i32 70,
	i32 33, i32 391, i32 400, i32 14, i32 141, i32 247, i32 38, i32 426,
	i32 215, i32 267, i32 180, i32 413, i32 430, i32 136, i32 94, i32 90,
	i32 152, i32 327, i32 385, i32 419, i32 24, i32 140, i32 57, i32 383,
	i32 375, i32 385, i32 142, i32 348, i32 51, i32 398, i32 229, i32 29,
	i32 160, i32 34, i32 167, i32 199, i32 217, i32 282, i32 225, i32 52,
	i32 213, i32 435, i32 325, i32 92, i32 237, i32 263, i32 35, i32 341,
	i32 241, i32 401, i32 160, i32 215, i32 9, i32 399, i32 78, i32 59,
	i32 382, i32 55, i32 212, i32 232, i32 395, i32 361, i32 230, i32 13,
	i32 324, i32 201, i32 260, i32 111, i32 357, i32 297, i32 32, i32 106,
	i32 86, i32 94, i32 53, i32 98, i32 333, i32 58, i32 9, i32 104,
	i32 275, i32 69, i32 323, i32 248, i32 394, i32 236, i32 248, i32 235,
	i32 211, i32 127, i32 309, i32 118, i32 137, i32 229, i32 311, i32 228,
	i32 128, i32 108, i32 386, i32 335, i32 133, i32 262, i32 332, i32 150,
	i32 159, i32 283, i32 271, i32 243, i32 204, i32 278, i32 309, i32 99,
	i32 24, i32 187, i32 314, i32 146, i32 216, i32 301, i32 242, i32 3,
	i32 343, i32 170, i32 193, i32 259, i32 102, i32 164, i32 101, i32 434,
	i32 273, i32 25, i32 95, i32 171, i32 175, i32 254, i32 3, i32 413,
	i32 318, i32 280, i32 376, i32 431, i32 1, i32 116, i32 335, i32 196,
	i32 283, i32 290, i32 247, i32 33, i32 6, i32 208, i32 417, i32 159,
	i32 366, i32 390, i32 415, i32 53, i32 87, i32 322, i32 344, i32 306,
	i32 44, i32 289, i32 379, i32 106, i32 365, i32 355, i32 47, i32 140,
	i32 240, i32 299, i32 65, i32 197, i32 300, i32 70, i32 82, i32 60,
	i32 91, i32 157, i32 260, i32 135, i32 112, i32 59, i32 407, i32 300,
	i32 229, i32 307, i32 174, i32 136, i32 354, i32 143, i32 40, i32 394,
	i32 245, i32 203, i32 228, i32 230, i32 61, i32 179, i32 203, i32 296,
	i32 81, i32 25, i32 36, i32 101, i32 293, i32 72, i32 379, i32 22,
	i32 271, i32 234, i32 360, i32 418, i32 123, i32 428, i32 70, i32 109,
	i32 424, i32 121, i32 119, i32 285, i32 302, i32 357, i32 286, i32 11,
	i32 2, i32 126, i32 242, i32 117, i32 145, i32 236, i32 41, i32 89,
	i32 362, i32 433, i32 255, i32 244, i32 177, i32 216, i32 27, i32 151,
	i32 370, i32 203, i32 408, i32 206, i32 331, i32 357, i32 254, i32 1,
	i32 256, i32 44, i32 270, i32 152, i32 430, i32 18, i32 392, i32 88,
	i32 241, i32 396, i32 41, i32 343, i32 289, i32 264, i32 364, i32 294,
	i32 96, i32 218, i32 28, i32 351, i32 41, i32 80, i32 188, i32 279,
	i32 267, i32 147, i32 110, i32 265, i32 11, i32 107, i32 369, i32 139,
	i32 16, i32 124, i32 67, i32 160, i32 22, i32 245, i32 398, i32 339,
	i32 104, i32 206, i32 338, i32 64, i32 58, i32 231, i32 397, i32 112,
	i32 177, i32 391, i32 429, i32 305, i32 336, i32 9, i32 328, i32 122,
	i32 100, i32 107, i32 76, i32 385, i32 297, i32 350, i32 230, i32 113,
	i32 257, i32 49, i32 59, i32 20, i32 296, i32 274, i32 73, i32 269,
	i32 158, i32 39, i32 396, i32 189, i32 35, i32 38, i32 402, i32 245,
	i32 361, i32 110, i32 191, i32 411, i32 21, i32 384, i32 333, i32 386,
	i32 295, i32 234, i32 15, i32 223, i32 81, i32 81, i32 274, i32 223,
	i32 375, i32 303, i32 366, i32 312, i32 155, i32 21, i32 374, i32 181,
	i32 232, i32 395, i32 50, i32 51, i32 359, i32 224, i32 421, i32 411,
	i32 96, i32 250, i32 210, i32 377, i32 407, i32 16, i32 273, i32 125,
	i32 404, i32 163, i32 45, i32 331, i32 182, i32 118, i32 64, i32 198,
	i32 169, i32 383, i32 209, i32 201, i32 14, i32 310, i32 113, i32 257,
	i32 61, i32 205, i32 76, i32 123, i32 410, i32 2, i32 420, i32 351,
	i32 282, i32 295, i32 193, i32 193, i32 388, i32 191, i32 181, i32 217,
	i32 334, i32 187, i32 295, i32 6, i32 264, i32 400, i32 278, i32 194,
	i32 226, i32 17, i32 418, i32 397, i32 79, i32 268, i32 189, i32 302,
	i32 133, i32 333, i32 178, i32 410, i32 185, i32 85, i32 220, i32 12,
	i32 34, i32 121, i32 339, i32 290, i32 176, i32 280, i32 432, i32 388,
	i32 87, i32 249, i32 18, i32 323, i32 387, i32 202, i32 288, i32 73,
	i32 97, i32 365, i32 246, i32 186, i32 168, i32 192, i32 284, i32 84,
	i32 426, i32 258, i32 263, i32 157, i32 36, i32 154, i32 422, i32 225,
	i32 425, i32 212, i32 147, i32 56, i32 115, i32 197, i32 239, i32 264,
	i32 320, i32 319, i32 378, i32 37, i32 426, i32 201, i32 117, i32 215,
	i32 256, i32 14, i32 250, i32 196, i32 149, i32 43, i32 233, i32 342,
	i32 254, i32 100, i32 338, i32 171, i32 16, i32 311, i32 48, i32 109,
	i32 99, i32 199, i32 300, i32 180, i32 27, i32 130, i32 29, i32 402,
	i32 362, i32 211, i32 312, i32 130, i32 44, i32 274, i32 198, i32 279,
	i32 152, i32 8, i32 318, i32 235, i32 301, i32 403, i32 416, i32 244,
	i32 415, i32 134, i32 414, i32 341, i32 365, i32 42, i32 339, i32 244,
	i32 33, i32 435, i32 46, i32 146, i32 296, i32 231, i32 372, i32 287,
	i32 361, i32 381, i32 275, i32 205, i32 140, i32 63, i32 134, i32 394,
	i32 48, i32 352, i32 163, i32 367, i32 261, i32 287, i32 250, i32 285,
	i32 363, i32 410, i32 319, i32 46, i32 167, i32 227, i32 176, i32 284,
	i32 227, i32 370, i32 399, i32 281, i32 406, i32 234, i32 18, i32 8,
	i32 366, i32 389, i32 182, i32 272, i32 311, i32 126, i32 373, i32 60,
	i32 144, i32 179, i32 304, i32 347, i32 409, i32 291, i32 349, i32 329,
	i32 325, i32 153, i32 145, i32 358, i32 337, i32 348, i32 384, i32 128,
	i32 336, i32 364, i32 163, i32 165, i32 432, i32 236, i32 276, i32 253,
	i32 202, i32 376, i32 350, i32 305, i32 412, i32 26, i32 358, i32 301,
	i32 288, i32 428, i32 376, i32 214, i32 84, i32 325, i32 129, i32 330,
	i32 103, i32 151, i32 328, i32 326, i32 306, i32 54, i32 165, i32 240,
	i32 170, i32 192, i32 133, i32 213, i32 349, i32 37, i32 321, i32 409,
	i32 209, i32 179, i32 185, i32 22, i32 114, i32 92, i32 50, i32 61,
	i32 124, i32 85, i32 129, i32 166, i32 373, i32 329, i32 169, i32 310,
	i32 313, i32 277, i32 249, i32 292, i32 4, i32 286, i32 405, i32 173,
	i32 2, i32 214, i32 297, i32 345, i32 118, i32 432, i32 226, i32 255,
	i32 19, i32 178, i32 219, i32 91, i32 66, i32 30, i32 207, i32 356,
	i32 398, i32 238, i32 269, i32 60, i32 113, i32 288, i32 32, i32 130,
	i32 162, i32 416, i32 267, i32 143, i32 378, i32 431, i32 412, i32 156,
	i32 17, i32 266, i32 252, i32 77, i32 75, i32 15, i32 172, i32 367,
	i32 87, i32 354, i32 198, i32 126, i32 371, i32 287, i32 298, i32 268,
	i32 419, i32 294, i32 34, i32 343, i32 184, i32 120, i32 141, i32 124,
	i32 390, i32 108, i32 396, i32 0, i32 321, i32 349, i32 263, i32 373,
	i32 403, i32 393, i32 391, i32 359, i32 54, i32 47, i32 28, i32 142,
	i32 148, i32 219, i32 150, i32 346, i32 35, i32 419, i32 177, i32 217,
	i32 77, i32 427, i32 164, i32 1, i32 299, i32 314, i32 415, i32 408,
	i32 162, i32 12, i32 158, i32 187, i32 154, i32 78, i32 352, i32 105,
	i32 114, i32 346, i32 246, i32 260, i32 189, i32 183, i32 66, i32 67,
	i32 324, i32 45, i32 262, i32 111, i32 7, i32 259, i32 55, i32 255,
	i32 65, i32 355, i32 393, i32 272, i32 20, i32 111, i32 103, i32 63,
	i32 145, i32 347, i32 195, i32 183, i32 253, i32 7, i32 227, i32 408,
	i32 173, i32 50, i32 324, i32 117, i32 208, i32 144, i32 182, i32 169,
	i32 389, i32 352, i32 82, i32 0, i32 115, i32 195, i32 298, i32 200,
	i32 17, i32 74, i32 303, i32 91, i32 251, i32 89, i32 122, i32 317,
	i32 380, i32 204, i32 257, i32 178, i32 305, i32 137, i32 156, i32 108,
	i32 11, i32 92, i32 433, i32 0, i32 378, i32 31, i32 186, i32 200,
	i32 421, i32 138, i32 413, i32 416, i32 315, i32 197, i32 248, i32 252,
	i32 40, i32 435, i32 237, i32 314, i32 141, i32 338, i32 340, i32 25,
	i32 176, i32 425, i32 74, i32 284, i32 316, i32 27, i32 68, i32 90,
	i32 384, i32 97, i32 115, i32 371, i32 31, i32 106, i32 341, i32 286,
	i32 37, i32 180, i32 210, i32 73, i32 186, i32 382, i32 332, i32 110,
	i32 125, i32 259, i32 89, i32 218, i32 88, i32 407, i32 95, i32 367,
	i32 196, i32 207, i32 212, i32 131, i32 298, i32 192, i32 317, i32 220,
	i32 310, i32 272, i32 316, i32 269, i32 382, i32 329, i32 202, i32 166,
	i32 132, i32 219, i32 322, i32 326, i32 307, i32 206, i32 10, i32 49,
	i32 423, i32 93, i32 241, i32 423, i32 153, i32 63, i32 138, i32 153,
	i32 62, i32 342, i32 372, i32 218, i32 429, i32 181, i32 358, i32 119,
	i32 139, i32 334, i32 86, i32 425, i32 162, i32 319, i32 146, i32 404,
	i32 281, i32 427, i32 84, i32 76, i32 71, i32 258, i32 138, i32 270,
	i32 251, i32 428, i32 127, i32 54, i32 112, i32 132, i32 90, i32 23,
	i32 356, i32 75, i32 131, i32 31, i32 216, i32 74, i32 293, i32 406,
	i32 161, i32 23, i32 4, i32 188, i32 173, i32 414, i32 125, i32 429,
	i32 273, i32 405, i32 400, i32 116, i32 191, i32 362, i32 175, i32 32,
	i32 3, i32 434, i32 167, i32 433, i32 344, i32 320, i32 347, i32 30,
	i32 19, i32 222, i32 381, i32 292, i32 95, i32 36, i32 5, i32 184,
	i32 261, i32 336, i32 158, i32 315, i32 213, i32 331, i32 377, i32 268,
	i32 434, i32 431, i32 183, i32 387, i32 322, i32 78, i32 64, i32 304,
	i32 380, i32 150, i32 265, i32 375, i32 123, i32 136, i32 318, i32 332,
	i32 233, i32 102, i32 39, i32 253, i32 142, i32 399, i32 69, i32 26,
	i32 383, i32 77, i32 327, i32 80, i32 291, i32 231, i32 24, i32 155,
	i32 38, i32 299, i32 412, i32 344, i32 261, i32 135, i32 105, i32 328,
	i32 57, i32 168, i32 93, i32 62, i32 363, i32 345, i32 377, i32 134,
	i32 239, i32 46, i32 374, i32 135, i32 276, i32 148, i32 190, i32 80,
	i32 270, i32 228, i32 292, i32 226, i32 205, i32 327, i32 222, i32 157,
	i32 397, i32 386, i32 85, i32 424, i32 235, i32 422, i32 62, i32 98,
	i32 308, i32 156, i32 403, i32 120, i32 427, i32 221, i32 6, i32 15,
	i32 75, i32 211, i32 393, i32 381, i32 149, i32 242, i32 52, i32 238,
	i32 243, i32 71, i32 23, i32 161, i32 128, i32 66, i32 114, i32 303,
	i32 290, i32 55, i32 53, i32 277, i32 188, i32 109, i32 137, i32 282,
	i32 291, i32 82, i32 285, i32 204, i32 421, i32 289, i32 359, i32 388,
	i32 131, i32 65, i32 379, i32 155
], align 4

@marshal_methods_number_of_classes = dso_local local_unnamed_addr constant i32 0, align 4

@marshal_methods_class_cache = dso_local local_unnamed_addr global [0 x %struct.MarshalMethodsManagedClass] zeroinitializer, align 8

; Names of classes in which marshal methods reside
@mm_class_names = dso_local local_unnamed_addr constant [0 x ptr] zeroinitializer, align 8

@mm_method_names = dso_local local_unnamed_addr constant [1 x %struct.MarshalMethodName] [
	%struct.MarshalMethodName {
		i64 u0x0000000000000000, ; name: 
		ptr @.MarshalMethodName.0_name; char* name
	} ; 0
], align 8

; get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr)
@get_function_pointer = internal dso_local unnamed_addr global ptr null, align 8

; Functions

; Function attributes: memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" uwtable willreturn
define void @xamarin_app_init(ptr nocapture noundef readnone %env, ptr noundef %fn) local_unnamed_addr #0
{
	%fnIsNull = icmp eq ptr %fn, null
	br i1 %fnIsNull, label %1, label %2

1: ; preds = %0
	%putsResult = call noundef i32 @puts(ptr @.mm.0)
	call void @abort()
	unreachable 

2: ; preds = %1, %0
	store ptr %fn, ptr @get_function_pointer, align 8, !tbaa !3
	ret void
}

; Strings
@.mm.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 1

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+fix-cortex-a53-835769,+neon,+outline-atomics,+v8a" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+fix-cortex-a53-835769,+neon,+outline-atomics,+v8a" }

; Metadata
!llvm.module.flags = !{!0, !1, !7, !8, !9, !10}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!".NET for Android remotes/origin/release/10.0.1xx @ 9a2d211ba972d3a0c4c108e043def432f3ec2620"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
!7 = !{i32 1, !"branch-target-enforcement", i32 0}
!8 = !{i32 1, !"sign-return-address", i32 0}
!9 = !{i32 1, !"sign-return-address-all", i32 0}
!10 = !{i32 1, !"sign-return-address-with-bkey", i32 0}
