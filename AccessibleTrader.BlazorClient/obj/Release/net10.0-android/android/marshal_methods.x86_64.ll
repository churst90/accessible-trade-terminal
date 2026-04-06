; ModuleID = 'marshal_methods.x86_64.ll'
source_filename = "marshal_methods.x86_64.ll"
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [268 x ptr] zeroinitializer, align 16

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [804 x i64] [
	i64 u0x001e58127c546039, ; 0: lib_System.Globalization.dll.so => 206
	i64 u0x003f21f150fd9b84, ; 1: lib-cs-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 39
	i64 u0x0071cf2d27b7d61e, ; 2: lib_Xamarin.AndroidX.SwipeRefreshLayout.dll.so => 170
	i64 u0x01109b0e4d99e61f, ; 3: System.ComponentModel.Annotations.dll => 193
	i64 u0x02a4c5a44384f885, ; 4: Microsoft.Extensions.Caching.Memory => 105
	i64 u0x02abedc11addc1ed, ; 5: lib_Mono.Android.Runtime.dll.so => 266
	i64 u0x031c967c64bb4b18, ; 6: AccessibleTrader.Plugins.Bitstamp => 181
	i64 u0x032267b2a94db371, ; 7: lib_Xamarin.AndroidX.AppCompat.dll.so => 148
	i64 u0x0363ac97a4cb84e6, ; 8: SQLitePCLRaw.provider.e_sqlite3.dll => 144
	i64 u0x043032f1d071fae0, ; 9: ru/Microsoft.Maui.Controls.resources => 76
	i64 u0x044440a55165631e, ; 10: lib-cs-Microsoft.Maui.Controls.resources.dll.so => 54
	i64 u0x046eb1581a80c6b0, ; 11: vi/Microsoft.Maui.Controls.resources => 82
	i64 u0x0470607fd33c32db, ; 12: Microsoft.IdentityModel.Abstractions.dll => 123
	i64 u0x0517ef04e06e9f76, ; 13: System.Net.Primitives => 220
	i64 u0x0565d18c6da3de38, ; 14: Xamarin.AndroidX.RecyclerView => 167
	i64 u0x057bf9fa9fb09f7c, ; 15: Microsoft.Data.Sqlite.dll => 99
	i64 u0x0581db89237110e9, ; 16: lib_System.Collections.dll.so => 192
	i64 u0x05989cb940b225a9, ; 17: Microsoft.Maui.dll => 130
	i64 u0x05ef98b6a1db882c, ; 18: lib_Microsoft.Data.Sqlite.dll.so => 99
	i64 u0x06076b5d2b581f08, ; 19: zh-HK/Microsoft.Maui.Controls.resources => 83
	i64 u0x06388ffe9f6c161a, ; 20: System.Xml.Linq.dll => 258
	i64 u0x0680a433c781bb3d, ; 21: Xamarin.AndroidX.Collection.Jvm => 151
	i64 u0x0690533f9fc14683, ; 22: lib_Microsoft.AspNetCore.Components.dll.so => 91
	i64 u0x07c57877c7ba78ad, ; 23: ru/Microsoft.Maui.Controls.resources.dll => 76
	i64 u0x07dcdc7460a0c5e4, ; 24: System.Collections.NonGeneric => 190
	i64 u0x08a7c865576bbde7, ; 25: System.Reflection.Primitives => 236
	i64 u0x08f3c9788ee2153c, ; 26: Xamarin.AndroidX.DrawerLayout => 156
	i64 u0x09138715c92dba90, ; 27: lib_System.ComponentModel.Annotations.dll.so => 193
	i64 u0x0919c28b89381a0b, ; 28: lib_Microsoft.Extensions.Options.dll.so => 121
	i64 u0x092266563089ae3e, ; 29: lib_System.Collections.NonGeneric.dll.so => 190
	i64 u0x092a18ee63d56e73, ; 30: de/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 27
	i64 u0x09d144a7e214d457, ; 31: System.Security.Cryptography => 246
	i64 u0x09e2b9f743db21a8, ; 32: lib_System.Reflection.Metadata.dll.so => 235
	i64 u0x0a805f95d98f597b, ; 33: lib_Microsoft.Extensions.Caching.Abstractions.dll.so => 104
	i64 u0x0abb3e2b271edc45, ; 34: System.Threading.Channels.dll => 252
	i64 u0x0b3b632c3bbee20c, ; 35: sk/Microsoft.Maui.Controls.resources => 77
	i64 u0x0b6aff547b84fbe9, ; 36: Xamarin.KotlinX.Serialization.Core.Jvm => 177
	i64 u0x0be2e1f8ce4064ed, ; 37: Xamarin.AndroidX.ViewPager => 171
	i64 u0x0c3ca6cc978e2aae, ; 38: pt-BR/Microsoft.Maui.Controls.resources => 73
	i64 u0x0c59ad9fbbd43abe, ; 39: Mono.Android => 267
	i64 u0x0c7790f60165fc06, ; 40: lib_Microsoft.Maui.Essentials.dll.so => 131
	i64 u0x0cce4bce83380b7f, ; 41: Xamarin.AndroidX.Security.SecurityCrypto => 169
	i64 u0x0cf6a95dadccbb9c, ; 42: zh-Hant/Microsoft.CodeAnalysis.resources.dll => 12
	i64 u0x0d86063f20335c36, ; 43: lib_Skender.Stock.Indicators.dll.so => 136
	i64 u0x0e14e73a54dda68e, ; 44: lib_System.Net.NameResolution.dll.so => 218
	i64 u0x0e7acf675d09f75a, ; 45: it/Microsoft.CodeAnalysis.resources => 4
	i64 u0x0ec01b05613190b9, ; 46: SkiaSharp.Views.Android.dll => 138
	i64 u0x0ec47e16319c99d9, ; 47: lib-de-Microsoft.CodeAnalysis.resources.dll.so => 1
	i64 u0x102a31b45304b1da, ; 48: Xamarin.AndroidX.CustomView => 155
	i64 u0x105b053cfbaba1f0, ; 49: lib_Microsoft.CodeAnalysis.dll.so => 95
	i64 u0x10a579e648829775, ; 50: Microsoft.CodeAnalysis => 95
	i64 u0x10e259b85a28bd6c, ; 51: de/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 27
	i64 u0x10f6cfcbcf801616, ; 52: System.IO.Compression.Brotli => 207
	i64 u0x114df3ff11650a65, ; 53: ru/Microsoft.CodeAnalysis.CSharp.resources => 22
	i64 u0x114f9c84eae55607, ; 54: zh-Hant/Microsoft.CodeAnalysis.Scripting.resources.dll => 51
	i64 u0x11a70d0e1009fb11, ; 55: System.Net.WebSockets.dll => 227
	i64 u0x1208da3842d90ff3, ; 56: lib-ko-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 19
	i64 u0x123639456fb056da, ; 57: System.Reflection.Emit.Lightweight.dll => 234
	i64 u0x125b7f94acb989db, ; 58: Xamarin.AndroidX.RecyclerView.dll => 167
	i64 u0x12771c3b6490ec33, ; 59: ja/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 31
	i64 u0x131463e9417f52d4, ; 60: de/Microsoft.CodeAnalysis.CSharp.resources => 14
	i64 u0x1393617ead22674a, ; 61: zh-Hant/Microsoft.CodeAnalysis.resources => 12
	i64 u0x139989e2fa3384e8, ; 62: ru/Microsoft.CodeAnalysis.Scripting.resources => 48
	i64 u0x13a01de0cbc3f06c, ; 63: lib-fr-Microsoft.Maui.Controls.resources.dll.so => 60
	i64 u0x13f1e5e209e91af4, ; 64: lib_Java.Interop.dll.so => 265
	i64 u0x13f1e880c25d96d1, ; 65: he/Microsoft.Maui.Controls.resources => 61
	i64 u0x143d8ea60a6a4011, ; 66: Microsoft.Extensions.DependencyInjection.Abstractions => 109
	i64 u0x1446c7a06695f3ea, ; 67: ko/Microsoft.CodeAnalysis.CSharp.resources.dll => 19
	i64 u0x1497051b917530bd, ; 68: lib_System.Net.WebSockets.dll.so => 227
	i64 u0x1506378c0000a92a, ; 69: lib-tr-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 23
	i64 u0x154f63b58010b003, ; 70: lib-zh-Hant-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 51
	i64 u0x16054fdcb6b3098b, ; 71: Microsoft.Extensions.DependencyModel.dll => 110
	i64 u0x17125c9a85b4929f, ; 72: lib_netstandard.dll.so => 263
	i64 u0x17b56e25558a5d36, ; 73: lib-hu-Microsoft.Maui.Controls.resources.dll.so => 64
	i64 u0x17b9399fe6696d5c, ; 74: lib-tr-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 49
	i64 u0x17f9358913beb16a, ; 75: System.Text.Encodings.Web => 249
	i64 u0x1805f780a2be57b5, ; 76: Polly.Core.dll => 135
	i64 u0x18402a709e357f3b, ; 77: lib_Xamarin.KotlinX.Serialization.Core.Jvm.dll.so => 177
	i64 u0x18950fae1c2bc98e, ; 78: lib-cs-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 13
	i64 u0x18f0ce884e87d89a, ; 79: nb/Microsoft.Maui.Controls.resources.dll => 70
	i64 u0x192712eaa333180f, ; 80: lib-zh-Hant-Microsoft.CodeAnalysis.resources.dll.so => 12
	i64 u0x19a4c090f14ebb66, ; 81: System.Security.Claims => 245
	i64 u0x1a761daba47c6ad5, ; 82: ja/Microsoft.CodeAnalysis.resources.dll => 5
	i64 u0x1a91866a319e9259, ; 83: lib_System.Collections.Concurrent.dll.so => 188
	i64 u0x1a9e139e4762aaf8, ; 84: es/Microsoft.CodeAnalysis.CSharp.resources.dll => 15
	i64 u0x1aac34d1917ba5d3, ; 85: lib_System.dll.so => 262
	i64 u0x1aad60783ffa3e5b, ; 86: lib-th-Microsoft.Maui.Controls.resources.dll.so => 79
	i64 u0x1c074bdeeae2e1c9, ; 87: lib-pl-Microsoft.CodeAnalysis.resources.dll.so => 7
	i64 u0x1c292b1598348d77, ; 88: Microsoft.Extensions.Diagnostics.dll => 111
	i64 u0x1c5217a9e4973753, ; 89: lib_Microsoft.Extensions.FileProviders.Physical.dll.so => 115
	i64 u0x1c753b5ff15bce1b, ; 90: Mono.Android.Runtime.dll => 266
	i64 u0x1d88944b182faec5, ; 91: fr/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 29
	i64 u0x1da4110562816681, ; 92: Xamarin.AndroidX.Security.SecurityCrypto.dll => 169
	i64 u0x1dbb0c2c6a999acb, ; 93: System.Diagnostics.StackTrace => 200
	i64 u0x1e3d87657e9659bc, ; 94: Xamarin.AndroidX.Navigation.UI => 166
	i64 u0x1e71143913d56c10, ; 95: lib-ko-Microsoft.Maui.Controls.resources.dll.so => 68
	i64 u0x1e7c31185e2fb266, ; 96: lib_System.Threading.Tasks.Parallel.dll.so => 253
	i64 u0x1ed8fcce5e9b50a0, ; 97: Microsoft.Extensions.Options.dll => 121
	i64 u0x1f59668779696d49, ; 98: lib-pl-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 33
	i64 u0x1fc7dd520ea7d6b9, ; 99: lib-pl-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 46
	i64 u0x209375905fcc1bad, ; 100: lib_System.IO.Compression.Brotli.dll.so => 207
	i64 u0x2110167c128cba15, ; 101: System.Globalization => 206
	i64 u0x2174319c0d835bc9, ; 102: System.Runtime => 244
	i64 u0x217470d3ba7d3c8c, ; 103: lib-de-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 27
	i64 u0x21cc7e445dcd5469, ; 104: System.Reflection.Emit.ILGeneration => 233
	i64 u0x220fd4f2e7c48170, ; 105: th/Microsoft.Maui.Controls.resources => 79
	i64 u0x237be844f1f812c7, ; 106: System.Threading.Thread.dll => 254
	i64 u0x23807c59646ec4f3, ; 107: lib_Microsoft.EntityFrameworkCore.dll.so => 100
	i64 u0x2407aef2bbe8fadf, ; 108: System.Console => 197
	i64 u0x240abe014b27e7d3, ; 109: Xamarin.AndroidX.Core.dll => 153
	i64 u0x245ebc45bf698558, ; 110: ru/Microsoft.CodeAnalysis.resources.dll => 9
	i64 u0x247619fe4413f8bf, ; 111: System.Runtime.Serialization.Primitives.dll => 243
	i64 u0x252073cc3caa62c2, ; 112: fr/Microsoft.Maui.Controls.resources.dll => 60
	i64 u0x256b8d41255f01b1, ; 113: Xamarin.Google.Crypto.Tink.Android => 174
	i64 u0x259e27cab50e0c41, ; 114: lib-es-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 28
	i64 u0x25a0a7eff76ea08e, ; 115: SQLitePCLRaw.batteries_v2.dll => 141
	i64 u0x2604a92c4e777626, ; 116: it/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 30
	i64 u0x2662c629b96b0b30, ; 117: lib_Xamarin.Kotlin.StdLib.dll.so => 175
	i64 u0x268c1439f13bcc29, ; 118: lib_Microsoft.Extensions.Primitives.dll.so => 122
	i64 u0x270a44600c921861, ; 119: System.IdentityModel.Tokens.Jwt => 145
	i64 u0x272377f9edc266a2, ; 120: tr/Microsoft.CodeAnalysis.resources => 10
	i64 u0x273f3515de5faf0d, ; 121: id/Microsoft.Maui.Controls.resources.dll => 65
	i64 u0x2742545f9094896d, ; 122: hr/Microsoft.Maui.Controls.resources => 63
	i64 u0x2759af78ab94d39b, ; 123: System.Net.WebSockets => 227
	i64 u0x27b2b16f3e9de038, ; 124: Xamarin.Google.Crypto.Tink.Android.dll => 174
	i64 u0x27b410442fad6cf1, ; 125: Java.Interop.dll => 265
	i64 u0x2801845a2c71fbfb, ; 126: System.Net.Primitives.dll => 220
	i64 u0x28e52865585a1ebe, ; 127: Microsoft.Extensions.Diagnostics.Abstractions.dll => 112
	i64 u0x2927d345f3daec35, ; 128: SkiaSharp.dll => 137
	i64 u0x29aeab763a527e52, ; 129: lib_Xamarin.AndroidX.Navigation.Common.Android.dll.so => 163
	i64 u0x2a128783efe70ba0, ; 130: uk/Microsoft.Maui.Controls.resources.dll => 81
	i64 u0x2a159655d05d918f, ; 131: es/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 28
	i64 u0x2a3b095612184159, ; 132: lib_System.Net.NetworkInformation.dll.so => 219
	i64 u0x2a6507a5ffabdf28, ; 133: System.Diagnostics.TraceSource.dll => 201
	i64 u0x2ac22cb244daa1d5, ; 134: zh-Hans/Microsoft.CodeAnalysis.Scripting.resources.dll => 50
	i64 u0x2ad156c8e1354139, ; 135: fi/Microsoft.Maui.Controls.resources => 59
	i64 u0x2af298f63581d886, ; 136: System.Text.RegularExpressions.dll => 251
	i64 u0x2af615542f04da50, ; 137: System.IdentityModel.Tokens.Jwt.dll => 145
	i64 u0x2afc1c4f898552ee, ; 138: lib_System.Formats.Asn1.dll.so => 205
	i64 u0x2b148910ed40fbf9, ; 139: zh-Hant/Microsoft.Maui.Controls.resources.dll => 85
	i64 u0x2b4d4904cebfa4e9, ; 140: Microsoft.Extensions.FileSystemGlobbing => 116
	i64 u0x2b8afb093774d1b8, ; 141: CryptoExchange.Net.dll => 89
	i64 u0x2c8bd14bb93a7d82, ; 142: lib-pl-Microsoft.Maui.Controls.resources.dll.so => 72
	i64 u0x2cbae5420bee4a22, ; 143: AccessibleTrader.Plugins.Bitstamp.dll => 181
	i64 u0x2cbd9262ca785540, ; 144: lib_System.Text.Encoding.CodePages.dll.so => 247
	i64 u0x2cc9e1fed6257257, ; 145: lib_System.Reflection.Emit.Lightweight.dll.so => 234
	i64 u0x2cd723e9fe623c7c, ; 146: lib_System.Private.Xml.Linq.dll.so => 231
	i64 u0x2d169d318a968379, ; 147: System.Threading.dll => 255
	i64 u0x2d42a09cd7c7d276, ; 148: lib-ja-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 44
	i64 u0x2d47774b7d993f59, ; 149: sv/Microsoft.Maui.Controls.resources.dll => 78
	i64 u0x2db915caf23548d2, ; 150: System.Text.Json.dll => 250
	i64 u0x2dd30aab497bcb81, ; 151: fr/Microsoft.CodeAnalysis.Scripting.resources => 42
	i64 u0x2df6accf4889fda3, ; 152: lib-pt-BR-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 34
	i64 u0x2e005eafd348fcc4, ; 153: ja/Microsoft.CodeAnalysis.Scripting.resources.dll => 44
	i64 u0x2e4d2e03e610a6e9, ; 154: pl/Microsoft.CodeAnalysis.resources => 7
	i64 u0x2e6f1f226821322a, ; 155: el/Microsoft.Maui.Controls.resources.dll => 57
	i64 u0x2e8ff3fae87a8245, ; 156: lib_Microsoft.JSInterop.dll.so => 127
	i64 u0x2f2e98e1c89b1aff, ; 157: System.Xml.ReaderWriter => 259
	i64 u0x2f5911d9ba814e4e, ; 158: System.Diagnostics.Tracing => 202
	i64 u0x2feb4d2fcda05cfd, ; 159: Microsoft.Extensions.Caching.Abstractions.dll => 104
	i64 u0x2ff49de6a71764a1, ; 160: lib_Microsoft.Extensions.Http.dll.so => 118
	i64 u0x309ee9eeec09a71e, ; 161: lib_Xamarin.AndroidX.Fragment.dll.so => 157
	i64 u0x309f2bedefa9a318, ; 162: Microsoft.IdentityModel.Abstractions => 123
	i64 u0x31195fef5d8fb552, ; 163: _Microsoft.Android.Resource.Designer.dll => 86
	i64 u0x31962e0f7c634c16, ; 164: Polly.Core => 135
	i64 u0x32243413e774362a, ; 165: Xamarin.AndroidX.CardView.dll => 150
	i64 u0x3235427f8d12dae1, ; 166: lib_System.Drawing.Primitives.dll.so => 203
	i64 u0x324622a9fd95b0c8, ; 167: lib-cs-Microsoft.CodeAnalysis.resources.dll.so => 0
	i64 u0x326256f7722d4fe5, ; 168: SkiaSharp.Views.Maui.Controls.dll => 139
	i64 u0x329753a17a517811, ; 169: fr/Microsoft.Maui.Controls.resources => 60
	i64 u0x32aa989ff07a84ff, ; 170: lib_System.Xml.ReaderWriter.dll.so => 259
	i64 u0x33642d5508314e46, ; 171: Microsoft.Extensions.FileSystemGlobbing.dll => 116
	i64 u0x33829542f112d59b, ; 172: System.Collections.Immutable => 189
	i64 u0x33a31443733849fe, ; 173: lib-es-Microsoft.Maui.Controls.resources.dll.so => 58
	i64 u0x341abc357fbb4ebf, ; 174: lib_System.Net.Sockets.dll.so => 223
	i64 u0x3496a1fbc5b6330d, ; 175: es/Microsoft.CodeAnalysis.Scripting.resources => 41
	i64 u0x34bd01fd4be06ee3, ; 176: lib_Microsoft.Extensions.FileProviders.Composite.dll.so => 114
	i64 u0x34dfd74fe2afcf37, ; 177: Microsoft.Maui => 130
	i64 u0x34e292762d9615df, ; 178: cs/Microsoft.Maui.Controls.resources.dll => 54
	i64 u0x34ef56e1435b2843, ; 179: pl/Microsoft.CodeAnalysis.CSharp.resources.dll => 20
	i64 u0x3508234247f48404, ; 180: Microsoft.Maui.Controls => 128
	i64 u0x353590da528c9d22, ; 181: System.ComponentModel.Annotations => 193
	i64 u0x3549870798b4cd30, ; 182: lib_Xamarin.AndroidX.ViewPager2.dll.so => 172
	i64 u0x355282fc1c909694, ; 183: Microsoft.Extensions.Configuration => 106
	i64 u0x355c649948d55d97, ; 184: lib_System.Runtime.Intrinsics.dll.so => 239
	i64 u0x35766456ffb7a7b4, ; 185: fr/Microsoft.CodeAnalysis.CSharp.resources.dll => 16
	i64 u0x36b2b50fdf589ae2, ; 186: System.Reflection.Emit.Lightweight => 234
	i64 u0x36cada77dc79928b, ; 187: System.IO.MemoryMappedFiles => 210
	i64 u0x374ef46b06791af6, ; 188: System.Reflection.Primitives.dll => 236
	i64 u0x380134e03b1e160a, ; 189: System.Collections.Immutable.dll => 189
	i64 u0x385c17636bb6fe6e, ; 190: Xamarin.AndroidX.CustomView.dll => 155
	i64 u0x38869c811d74050e, ; 191: System.Net.NameResolution.dll => 218
	i64 u0x393c226616977fdb, ; 192: lib_Xamarin.AndroidX.ViewPager.dll.so => 171
	i64 u0x395b3053dde89e41, ; 193: lib_System.Reactive.dll.so => 146
	i64 u0x395e37c3334cf82a, ; 194: lib-ca-Microsoft.Maui.Controls.resources.dll.so => 53
	i64 u0x39721dd6cab9d79e, ; 195: Polly.dll => 134
	i64 u0x39a87563fdb248a0, ; 196: System.Reactive.dll => 146
	i64 u0x39aa39fda111d9d3, ; 197: Newtonsoft.Json => 133
	i64 u0x39c3107c28752af1, ; 198: lib_Microsoft.Extensions.FileProviders.Abstractions.dll.so => 113
	i64 u0x3b860f9932505633, ; 199: lib_System.Text.Encoding.Extensions.dll.so => 248
	i64 u0x3be6248c2bc7dc8c, ; 200: Microsoft.JSInterop.dll => 127
	i64 u0x3be99b43dd39dd37, ; 201: Xamarin.AndroidX.SavedState.SavedState.Android => 168
	i64 u0x3bea9ebe8c027c01, ; 202: lib_Microsoft.IdentityModel.Tokens.dll.so => 126
	i64 u0x3c708e9b7a0ff300, ; 203: ko/Microsoft.CodeAnalysis.Scripting.resources => 45
	i64 u0x3c7c495f58ac5ee9, ; 204: Xamarin.Kotlin.StdLib => 175
	i64 u0x3d46f0b995082740, ; 205: System.Xml.Linq => 258
	i64 u0x3d9c2a242b040a50, ; 206: lib_Xamarin.AndroidX.Core.dll.so => 153
	i64 u0x3da7781d6333a8fe, ; 207: SQLitePCLRaw.batteries_v2 => 141
	i64 u0x3e7f8912b96e5065, ; 208: Microsoft.AspNetCore.Components.WebView.dll => 93
	i64 u0x3f6f5914291cdcf7, ; 209: Microsoft.Extensions.Hosting.Abstractions => 117
	i64 u0x40c6d9cbfdb8b9f7, ; 210: SkiaSharp.Views.Maui.Core.dll => 140
	i64 u0x415c502eb40e7418, ; 211: es/Microsoft.CodeAnalysis.resources.dll => 2
	i64 u0x41986970eb721f2b, ; 212: Microsoft.CodeAnalysis.Scripting.dll => 98
	i64 u0x41cab042be111c34, ; 213: lib_Xamarin.AndroidX.AppCompat.AppCompatResources.dll.so => 149
	i64 u0x4260fbb8d8266ed7, ; 214: AccessibleTrader.Sdk.dll => 185
	i64 u0x43375950ec7c1b6a, ; 215: netstandard.dll => 263
	i64 u0x434c4e1d9284cdae, ; 216: Mono.Android.dll => 267
	i64 u0x43950f84de7cc79a, ; 217: pl/Microsoft.Maui.Controls.resources.dll => 72
	i64 u0x4454e6d0b361addc, ; 218: lib-ru-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 35
	i64 u0x4489438e98f9c0f7, ; 219: AccessibleTrader.Plugins.Coinbase.dll => 182
	i64 u0x448bd33429269b19, ; 220: Microsoft.CSharp => 187
	i64 u0x4499fa3c8e494654, ; 221: lib_System.Runtime.Serialization.Primitives.dll.so => 243
	i64 u0x4515080865a951a5, ; 222: Xamarin.Kotlin.StdLib.dll => 175
	i64 u0x453c1277f85cf368, ; 223: lib_Microsoft.EntityFrameworkCore.Abstractions.dll.so => 101
	i64 u0x458d2df79ac57c1d, ; 224: lib_System.IdentityModel.Tokens.Jwt.dll.so => 145
	i64 u0x45c40276a42e283e, ; 225: System.Diagnostics.TraceSource => 201
	i64 u0x45fcc9fd66f25095, ; 226: Microsoft.Extensions.DependencyModel => 110
	i64 u0x46a4213bc97fe5ae, ; 227: lib-ru-Microsoft.Maui.Controls.resources.dll.so => 76
	i64 u0x47358bd471172e1d, ; 228: lib_System.Xml.Linq.dll.so => 258
	i64 u0x475461b41cd2bae5, ; 229: lib-zh-Hant-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 25
	i64 u0x47aaf174699667d9, ; 230: tr/Microsoft.CodeAnalysis.Scripting.resources.dll => 49
	i64 u0x47daf4e1afbada10, ; 231: pt/Microsoft.Maui.Controls.resources => 74
	i64 u0x480c0a47dd42dd81, ; 232: lib_System.IO.MemoryMappedFiles.dll.so => 210
	i64 u0x49e952f19a4e2022, ; 233: System.ObjectModel => 229
	i64 u0x4a1afd3bf9c69c98, ; 234: fr/Microsoft.CodeAnalysis.resources => 3
	i64 u0x4a5667b2462a664b, ; 235: lib_Xamarin.AndroidX.Navigation.UI.dll.so => 166
	i64 u0x4aaf23876d7335cb, ; 236: lib-zh-Hant-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 38
	i64 u0x4b484a0d637947d7, ; 237: lib-zh-Hans-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 24
	i64 u0x4b558744a6e1abe0, ; 238: lib-de-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 14
	i64 u0x4b7b6532ded934b7, ; 239: System.Text.Json => 250
	i64 u0x4bf547f87e5016a8, ; 240: lib_SkiaSharp.Views.Android.dll.so => 138
	i64 u0x4c2029a97af23a8d, ; 241: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android => 161
	i64 u0x4ca014ceac582c86, ; 242: Microsoft.EntityFrameworkCore.Relational.dll => 102
	i64 u0x4cc5f15266470798, ; 243: lib_Xamarin.AndroidX.Loader.dll.so => 162
	i64 u0x4cf6f67dc77aacd2, ; 244: System.Net.NetworkInformation.dll => 219
	i64 u0x4d3183dd245425d4, ; 245: System.Net.WebSockets.Client.dll => 226
	i64 u0x4d479f968a05e504, ; 246: System.Linq.Expressions.dll => 212
	i64 u0x4d55a010ffc4faff, ; 247: System.Private.Xml => 232
	i64 u0x4d95fccc1f67c7ca, ; 248: System.Runtime.Loader.dll => 240
	i64 u0x4dcf44c3c9b076a2, ; 249: it/Microsoft.Maui.Controls.resources.dll => 66
	i64 u0x4dd9247f1d2c3235, ; 250: Xamarin.AndroidX.Loader.dll => 162
	i64 u0x4df510084e2a0bae, ; 251: Microsoft.JSInterop => 127
	i64 u0x4e32f00cb0937401, ; 252: Mono.Android.Runtime => 266
	i64 u0x4e5eea4668ac2b18, ; 253: System.Text.Encoding.CodePages => 247
	i64 u0x4e84220084ab2d20, ; 254: cs/Microsoft.CodeAnalysis.CSharp.resources.dll => 13
	i64 u0x4ebd0c4b82c5eefc, ; 255: lib_System.Threading.Channels.dll.so => 252
	i64 u0x4f21ee6ef9eb527e, ; 256: ca/Microsoft.Maui.Controls.resources => 53
	i64 u0x4fd5f3ee53d0a4f0, ; 257: SQLitePCLRaw.lib.e_sqlite3.android => 143
	i64 u0x4ffd65baff757598, ; 258: Microsoft.IdentityModel.Tokens => 126
	i64 u0x5037f0be3c28c7a3, ; 259: lib_Microsoft.Maui.Controls.dll.so => 128
	i64 u0x50c3a29b21050d45, ; 260: System.Linq.Parallel.dll => 213
	i64 u0x5112ed116d87baf8, ; 261: CommunityToolkit.Mvvm => 88
	i64 u0x5131bbe80989093f, ; 262: Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll => 160
	i64 u0x516324a5050a7e3c, ; 263: System.Net.WebProxy => 225
	i64 u0x51bb8a2afe774e32, ; 264: System.Drawing => 204
	i64 u0x526ce79eb8e90527, ; 265: lib_System.Net.Primitives.dll.so => 220
	i64 u0x52808a6faff1589d, ; 266: zh-Hant/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 38
	i64 u0x52829f00b4467c38, ; 267: lib_System.Data.Common.dll.so => 198
	i64 u0x529ffe06f39ab8db, ; 268: Xamarin.AndroidX.Core => 153
	i64 u0x52ff996554dbf352, ; 269: Microsoft.Maui.Graphics => 132
	i64 u0x533514f6711b299b, ; 270: ko/Microsoft.CodeAnalysis.CSharp.resources => 19
	i64 u0x535f7e40e8fef8af, ; 271: lib-sk-Microsoft.Maui.Controls.resources.dll.so => 77
	i64 u0x539dd21b5958055b, ; 272: AccessibleTrader.Plugins.Alpaca => 179
	i64 u0x53a96d5c86c9e194, ; 273: System.Net.NetworkInformation => 219
	i64 u0x53be1038a61e8d44, ; 274: System.Runtime.InteropServices.RuntimeInformation.dll => 237
	i64 u0x53c3014b9437e684, ; 275: lib-zh-HK-Microsoft.Maui.Controls.resources.dll.so => 83
	i64 u0x5435e6f049e9bc37, ; 276: System.Security.Claims.dll => 245
	i64 u0x54795225dd1587af, ; 277: lib_System.Runtime.dll.so => 244
	i64 u0x54b851bc9b470503, ; 278: Xamarin.AndroidX.Navigation.Common.Android => 163
	i64 u0x54d75f85d6578cff, ; 279: lib-fr-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 16
	i64 u0x556e8b63b660ab8b, ; 280: Xamarin.AndroidX.Lifecycle.Common.Jvm.dll => 158
	i64 u0x5588627c9a108ec9, ; 281: System.Collections.Specialized => 191
	i64 u0x561449e1215a61e4, ; 282: lib_SkiaSharp.Views.Maui.Core.dll.so => 140
	i64 u0x56f76b6edb837f8b, ; 283: Polly => 134
	i64 u0x571c5cfbec5ae8e2, ; 284: System.Private.Uri => 230
	i64 u0x5724fbe6b45b7f07, ; 285: lib-pt-BR-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 21
	i64 u0x573a4253c76b0960, ; 286: AccessibleTrader.Plugins.Polygon => 184
	i64 u0x578cd35c91d7b347, ; 287: lib_SQLitePCLRaw.core.dll.so => 142
	i64 u0x579a06fed6eec900, ; 288: System.Private.CoreLib.dll => 264
	i64 u0x57adda3c951abb33, ; 289: Microsoft.Extensions.Hosting.Abstractions.dll => 117
	i64 u0x57c542c14049b66d, ; 290: System.Diagnostics.DiagnosticSource => 199
	i64 u0x582046896c5b64ca, ; 291: lib-fr-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 29
	i64 u0x58601b2dda4a27b9, ; 292: lib-ja-Microsoft.Maui.Controls.resources.dll.so => 67
	i64 u0x58688d9af496b168, ; 293: Microsoft.Extensions.DependencyInjection.dll => 108
	i64 u0x58ef0576630aa114, ; 294: fr/Microsoft.CodeAnalysis.CSharp.resources => 16
	i64 u0x595a356d23e8da9a, ; 295: lib_Microsoft.CSharp.dll.so => 187
	i64 u0x5a89a886ae30258d, ; 296: lib_Xamarin.AndroidX.CoordinatorLayout.dll.so => 152
	i64 u0x5a8f6699f4a1caa9, ; 297: lib_System.Threading.dll.so => 255
	i64 u0x5ab8ea45990c0132, ; 298: lib_AccessibleTrader.Plugins.Bitstamp.dll.so => 181
	i64 u0x5ae9cd33b15841bf, ; 299: System.ComponentModel => 196
	i64 u0x5b2384937958c860, ; 300: zh-Hant/Microsoft.CodeAnalysis.Scripting.resources => 51
	i64 u0x5b5ba1327561f926, ; 301: lib_SkiaSharp.Views.Maui.Controls.dll.so => 139
	i64 u0x5b5f0e240a06a2a2, ; 302: da/Microsoft.Maui.Controls.resources.dll => 55
	i64 u0x5bb93c3ef9525c89, ; 303: es/Microsoft.CodeAnalysis.resources => 2
	i64 u0x5be34cb3cc2ff949, ; 304: tr/Microsoft.CodeAnalysis.CSharp.resources => 23
	i64 u0x5c393624b8176517, ; 305: lib_Microsoft.Extensions.Logging.dll.so => 119
	i64 u0x5c6724284a5e7317, ; 306: lib-tr-Microsoft.CodeAnalysis.resources.dll.so => 10
	i64 u0x5d0a4a29b02d9d3c, ; 307: System.Net.WebHeaderCollection.dll => 224
	i64 u0x5d25ef991dd9a85c, ; 308: Microsoft.AspNetCore.Components.WebView.Maui.dll => 94
	i64 u0x5d7ec76c1c703055, ; 309: System.Threading.Tasks.Parallel => 253
	i64 u0x5db0cbbd1028510e, ; 310: lib_System.Runtime.InteropServices.dll.so => 238
	i64 u0x5db30905d3e5013b, ; 311: Xamarin.AndroidX.Collection.Jvm.dll => 151
	i64 u0x5e467bc8f09ad026, ; 312: System.Collections.Specialized.dll => 191
	i64 u0x5e8eb5167db0428b, ; 313: es/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 28
	i64 u0x5ea92fdb19ec8c4c, ; 314: System.Text.Encodings.Web.dll => 249
	i64 u0x5eb8046dd40e9ac3, ; 315: System.ComponentModel.Primitives => 194
	i64 u0x5f36ccf5c6a57e24, ; 316: System.Xml.ReaderWriter.dll => 259
	i64 u0x5f4294b9b63cb842, ; 317: System.Data.Common => 198
	i64 u0x5f7399e166075632, ; 318: lib_SQLitePCLRaw.lib.e_sqlite3.android.dll.so => 143
	i64 u0x5f9a2d823f664957, ; 319: lib-el-Microsoft.Maui.Controls.resources.dll.so => 57
	i64 u0x609f4b7b63d802d4, ; 320: lib_Microsoft.Extensions.DependencyInjection.dll.so => 108
	i64 u0x60cd4e33d7e60134, ; 321: Xamarin.KotlinX.Coroutines.Core.Jvm => 176
	i64 u0x60f62d786afcf130, ; 322: System.Memory => 216
	i64 u0x61be8d1299194243, ; 323: Microsoft.Maui.Controls.Xaml => 129
	i64 u0x61d2cba29557038f, ; 324: de/Microsoft.Maui.Controls.resources => 56
	i64 u0x61d88f399afb2f45, ; 325: lib_System.Runtime.Loader.dll.so => 240
	i64 u0x622eef6f9e59068d, ; 326: System.Private.CoreLib => 264
	i64 u0x62bffe14414fd6ca, ; 327: lib-zh-Hans-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 50
	i64 u0x6314e018e86b4f94, ; 328: AccessibleTrader.Plugins.Binance => 180
	i64 u0x639fb99a7bef11de, ; 329: Xamarin.AndroidX.Navigation.Runtime.Android.dll => 165
	i64 u0x63f1f6883c1e23c2, ; 330: lib_System.Collections.Immutable.dll.so => 189
	i64 u0x6400f68068c1e9f1, ; 331: Xamarin.Google.Android.Material.dll => 173
	i64 u0x6514d0e310a70ab0, ; 332: ja/Microsoft.CodeAnalysis.Scripting.resources => 44
	i64 u0x65d8ddec9a3de89e, ; 333: ru/Microsoft.CodeAnalysis.resources => 9
	i64 u0x65ecac39144dd3cc, ; 334: Microsoft.Maui.Controls.dll => 128
	i64 u0x65ece51227bfa724, ; 335: lib_System.Runtime.Numerics.dll.so => 241
	i64 u0x6692e924eade1b29, ; 336: lib_System.Console.dll.so => 197
	i64 u0x66a4e5c6a3fb0bae, ; 337: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll.so => 160
	i64 u0x66d13304ce1a3efa, ; 338: Xamarin.AndroidX.CursorAdapter => 154
	i64 u0x68558ec653afa616, ; 339: lib-da-Microsoft.Maui.Controls.resources.dll.so => 55
	i64 u0x6872ec7a2e36b1ac, ; 340: System.Drawing.Primitives.dll => 203
	i64 u0x68fbbbe2eb455198, ; 341: System.Formats.Asn1 => 205
	i64 u0x69063fc0ba8e6bdd, ; 342: he/Microsoft.Maui.Controls.resources.dll => 61
	i64 u0x699dffb2427a2d71, ; 343: SQLitePCLRaw.lib.e_sqlite3.android.dll => 143
	i64 u0x69c43767b6624bb2, ; 344: pl/Microsoft.CodeAnalysis.CSharp.resources => 20
	i64 u0x6a4d7577b2317255, ; 345: System.Runtime.InteropServices.dll => 238
	i64 u0x6abfbfb2796f4e84, ; 346: Microsoft.CodeAnalysis.CSharp => 96
	i64 u0x6ace3b74b15ee4a4, ; 347: nb/Microsoft.Maui.Controls.resources => 70
	i64 u0x6b613586f98094ed, ; 348: it/Microsoft.CodeAnalysis.Scripting.resources.dll => 43
	i64 u0x6c475a83367e34db, ; 349: ko/Microsoft.CodeAnalysis.Scripting.resources.dll => 45
	i64 u0x6d12bfaa99c72b1f, ; 350: lib_Microsoft.Maui.Graphics.dll.so => 132
	i64 u0x6d79993361e10ef2, ; 351: Microsoft.Extensions.Primitives => 122
	i64 u0x6d7eeca99577fc8b, ; 352: lib_System.Net.WebProxy.dll.so => 225
	i64 u0x6d8515b19946b6a2, ; 353: System.Net.WebProxy.dll => 225
	i64 u0x6d86d56b84c8eb71, ; 354: lib_Xamarin.AndroidX.CursorAdapter.dll.so => 154
	i64 u0x6d9bea6b3e895cf7, ; 355: Microsoft.Extensions.Primitives.dll => 122
	i64 u0x6e145bcac443aa70, ; 356: pt-BR/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 34
	i64 u0x6e25a02c3833319a, ; 357: lib_Xamarin.AndroidX.Navigation.Fragment.dll.so => 164
	i64 u0x6e2fb2ace98ab808, ; 358: zh-Hant/Microsoft.CodeAnalysis.CSharp.resources => 25
	i64 u0x6ed5cb5878424ad5, ; 359: pl/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 33
	i64 u0x6fa7ed9b35fd3dd3, ; 360: DynamicData => 90
	i64 u0x6fd2265da78b93a4, ; 361: lib_Microsoft.Maui.dll.so => 130
	i64 u0x6fdfc7de82c33008, ; 362: cs/Microsoft.Maui.Controls.resources => 54
	i64 u0x6ffc4967cc47ba57, ; 363: System.IO.FileSystem.Watcher.dll => 209
	i64 u0x7078c940a89ab2ee, ; 364: ja/Microsoft.CodeAnalysis.CSharp.resources => 18
	i64 u0x70e99f48c05cb921, ; 365: tr/Microsoft.Maui.Controls.resources.dll => 80
	i64 u0x70fd3deda22442d2, ; 366: lib-nb-Microsoft.Maui.Controls.resources.dll.so => 70
	i64 u0x717530326f808838, ; 367: lib_Microsoft.Extensions.Diagnostics.Abstractions.dll.so => 112
	i64 u0x71a248f3a13ad366, ; 368: tr/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 36
	i64 u0x71a495ea3761dde8, ; 369: lib-it-Microsoft.Maui.Controls.resources.dll.so => 66
	i64 u0x71ad672adbe48f35, ; 370: System.ComponentModel.Primitives.dll => 194
	i64 u0x72b1fb4109e08d7b, ; 371: lib-hr-Microsoft.Maui.Controls.resources.dll.so => 63
	i64 u0x72e0300099accce1, ; 372: System.Xml.XPath.XDocument => 261
	i64 u0x73e4ce94e2eb6ffc, ; 373: lib_System.Memory.dll.so => 216
	i64 u0x73f2645914262879, ; 374: lib_Microsoft.EntityFrameworkCore.Sqlite.dll.so => 103
	i64 u0x7444749b0fe2993c, ; 375: lib-fr-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 42
	i64 u0x746cf89b511b4d40, ; 376: lib_Microsoft.Extensions.Diagnostics.dll.so => 111
	i64 u0x755a91767330b3d4, ; 377: lib_Microsoft.Extensions.Configuration.dll.so => 106
	i64 u0x76ca07b878f44da0, ; 378: System.Runtime.Numerics.dll => 241
	i64 u0x778a805e625329ef, ; 379: System.Linq.Parallel => 213
	i64 u0x780bc73597a503a9, ; 380: lib-ms-Microsoft.Maui.Controls.resources.dll.so => 69
	i64 u0x783606d1e53e7a1a, ; 381: th/Microsoft.Maui.Controls.resources.dll => 79
	i64 u0x7888c8518f32343b, ; 382: tr/Microsoft.CodeAnalysis.resources.dll => 10
	i64 u0x78a45e51311409b6, ; 383: Xamarin.AndroidX.Fragment.dll => 157
	i64 u0x7947f4efc8e9966c, ; 384: Microsoft.CodeAnalysis.Scripting => 98
	i64 u0x795754f83b38b3ad, ; 385: lib_Microsoft.CodeAnalysis.Scripting.dll.so => 98
	i64 u0x796d689afabf16c2, ; 386: pt-BR/Microsoft.CodeAnalysis.Scripting.resources => 47
	i64 u0x7996e32deaf72986, ; 387: Microsoft.CodeAnalysis.CSharp.dll => 96
	i64 u0x79c9f0820bc356a5, ; 388: DynamicData.dll => 90
	i64 u0x7a25bdb29108c6e7, ; 389: Microsoft.Extensions.Http => 118
	i64 u0x7a71889545dcdb00, ; 390: lib_Microsoft.AspNetCore.Components.WebView.dll.so => 93
	i64 u0x7adb8da2ac89b647, ; 391: fi/Microsoft.Maui.Controls.resources.dll => 59
	i64 u0x7b150145c0a9058c, ; 392: Microsoft.Data.Sqlite => 99
	i64 u0x7b4927e421291c41, ; 393: Microsoft.IdentityModel.JsonWebTokens.dll => 124
	i64 u0x7bef86a4335c4870, ; 394: System.ComponentModel.TypeConverter => 195
	i64 u0x7c0820144cd34d6a, ; 395: sk/Microsoft.Maui.Controls.resources.dll => 77
	i64 u0x7c2a0bd1e0f988fc, ; 396: lib-de-Microsoft.Maui.Controls.resources.dll.so => 56
	i64 u0x7c60acf6404e96b6, ; 397: Xamarin.AndroidX.Navigation.Common.Android.dll => 163
	i64 u0x7d61f5eb85697e65, ; 398: Binance.Net => 87
	i64 u0x7d649b75d580bb42, ; 399: ms/Microsoft.Maui.Controls.resources.dll => 69
	i64 u0x7d8ee2bdc8e3aad1, ; 400: System.Numerics.Vectors => 228
	i64 u0x7dfc3d6d9d8d7b70, ; 401: System.Collections => 192
	i64 u0x7e2e564fa2f76c65, ; 402: lib_System.Diagnostics.Tracing.dll.so => 202
	i64 u0x7e302e110e1e1346, ; 403: lib_System.Security.Claims.dll.so => 245
	i64 u0x7e88d9bbdca2967e, ; 404: AccessibleTrader.Plugins.Fred.dll => 183
	i64 u0x7e8cdd26d9e30e7d, ; 405: lib-pt-BR-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 47
	i64 u0x7e946809d6008ef2, ; 406: lib_System.ObjectModel.dll.so => 229
	i64 u0x7ecc13347c8fd849, ; 407: lib_System.ComponentModel.dll.so => 196
	i64 u0x7f00ddd9b9ca5a13, ; 408: Xamarin.AndroidX.ViewPager.dll => 171
	i64 u0x7f8efcd9c2121403, ; 409: pl/Microsoft.CodeAnalysis.Scripting.resources => 46
	i64 u0x7f9351cd44b1273f, ; 410: Microsoft.Extensions.Configuration.Abstractions => 107
	i64 u0x7fbd557c99b3ce6f, ; 411: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.dll.so => 159
	i64 u0x80da183a87731838, ; 412: System.Reflection.Metadata => 235
	i64 u0x80ee53ea610b3f78, ; 413: zh-Hans/Microsoft.CodeAnalysis.CSharp.resources => 24
	i64 u0x80fa55b6d1b0be99, ; 414: SQLitePCLRaw.provider.e_sqlite3 => 144
	i64 u0x8101a73bd4533440, ; 415: Microsoft.AspNetCore.Components.Web => 92
	i64 u0x812c069d5cdecc17, ; 416: System.dll => 262
	i64 u0x813814e867226a33, ; 417: lib_Microsoft.CodeAnalysis.CSharp.Scripting.dll.so => 97
	i64 u0x81ab745f6c0f5ce6, ; 418: zh-Hant/Microsoft.Maui.Controls.resources => 85
	i64 u0x8277f2be6b5ce05f, ; 419: Xamarin.AndroidX.AppCompat => 148
	i64 u0x828f06563b30bc50, ; 420: lib_Xamarin.AndroidX.CardView.dll.so => 150
	i64 u0x82df8f5532a10c59, ; 421: lib_System.Drawing.dll.so => 204
	i64 u0x82f6403342e12049, ; 422: uk/Microsoft.Maui.Controls.resources => 81
	i64 u0x83a7afd2c49adc86, ; 423: lib_Microsoft.IdentityModel.Abstractions.dll.so => 123
	i64 u0x83c14ba66c8e2b8c, ; 424: zh-Hans/Microsoft.Maui.Controls.resources => 84
	i64 u0x83de69860da6cbdd, ; 425: Microsoft.Extensions.FileProviders.Composite => 114
	i64 u0x846ce984efea52c7, ; 426: System.Threading.Tasks.Parallel.dll => 253
	i64 u0x84cd5cdec0f54bcc, ; 427: lib_Microsoft.EntityFrameworkCore.Relational.dll.so => 102
	i64 u0x84f9060cc4a93c8f, ; 428: lib_SkiaSharp.dll.so => 137
	i64 u0x863a7c3aa6592d25, ; 429: zh-Hans/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 37
	i64 u0x86a909228dc7657b, ; 430: lib-zh-Hant-Microsoft.Maui.Controls.resources.dll.so => 85
	i64 u0x86b3e00c36b84509, ; 431: Microsoft.Extensions.Configuration.dll => 106
	i64 u0x8704193f462e892e, ; 432: lib_Microsoft.Extensions.FileSystemGlobbing.dll.so => 116
	i64 u0x87c4b8a492b176ad, ; 433: Microsoft.EntityFrameworkCore.Abstractions => 101
	i64 u0x87c69b87d9283884, ; 434: lib_System.Threading.Thread.dll.so => 254
	i64 u0x87f6569b25707834, ; 435: System.IO.Compression.Brotli.dll => 207
	i64 u0x8842b3a5d2d3fb36, ; 436: Microsoft.Maui.Essentials => 131
	i64 u0x88826e51a5d4a3d0, ; 437: de/Microsoft.CodeAnalysis.resources.dll => 1
	i64 u0x88bda98e0cffb7a9, ; 438: lib_Xamarin.KotlinX.Coroutines.Core.Jvm.dll.so => 176
	i64 u0x8930322c7bd8f768, ; 439: netstandard => 263
	i64 u0x897a606c9e39c75f, ; 440: lib_System.ComponentModel.Primitives.dll.so => 194
	i64 u0x898a5c6bc9e47ec1, ; 441: lib_Xamarin.AndroidX.SavedState.SavedState.Android.dll.so => 168
	i64 u0x89c5188089ec2cd5, ; 442: lib_System.Runtime.InteropServices.RuntimeInformation.dll.so => 237
	i64 u0x8a399a706fcbce4b, ; 443: Microsoft.Extensions.Caching.Abstractions => 104
	i64 u0x8ad229ea26432ee2, ; 444: Xamarin.AndroidX.Loader => 162
	i64 u0x8b226e2ff894c966, ; 445: lib_CryptoExchange.Net.dll.so => 89
	i64 u0x8b4ff5d0fdd5faa1, ; 446: lib_System.Diagnostics.DiagnosticSource.dll.so => 199
	i64 u0x8b9ceca7acae3451, ; 447: lib-he-Microsoft.Maui.Controls.resources.dll.so => 61
	i64 u0x8c39b02ed181787b, ; 448: pt-BR/Microsoft.CodeAnalysis.CSharp.resources => 21
	i64 u0x8c575135aa1ccef4, ; 449: Microsoft.Extensions.FileProviders.Abstractions => 113
	i64 u0x8d0f420977c2c1c7, ; 450: Xamarin.AndroidX.CursorAdapter.dll => 154
	i64 u0x8d52a25632e81824, ; 451: Microsoft.EntityFrameworkCore.Sqlite.dll => 103
	i64 u0x8d7b8ab4b3310ead, ; 452: System.Threading => 255
	i64 u0x8da188285aadfe8e, ; 453: System.Collections.Concurrent => 188
	i64 u0x8ec6e06a61c1baeb, ; 454: lib_Newtonsoft.Json.dll.so => 133
	i64 u0x8ee08b8194a30f48, ; 455: lib-hi-Microsoft.Maui.Controls.resources.dll.so => 62
	i64 u0x8ef7601039857a44, ; 456: lib-ro-Microsoft.Maui.Controls.resources.dll.so => 75
	i64 u0x8ef9414937d93a0a, ; 457: SQLitePCLRaw.core.dll => 142
	i64 u0x8f32c6f611f6ffab, ; 458: pt/Microsoft.Maui.Controls.resources.dll => 74
	i64 u0x8f8829d21c8985a4, ; 459: lib-pt-BR-Microsoft.Maui.Controls.resources.dll.so => 73
	i64 u0x8f8b0f07edd7b3b6, ; 460: cs/Microsoft.CodeAnalysis.resources.dll => 0
	i64 u0x8fa404e6277d0694, ; 461: zh-Hans/Microsoft.CodeAnalysis.CSharp.resources.dll => 24
	i64 u0x8fbf5b0114c6dcef, ; 462: System.Globalization.dll => 206
	i64 u0x8fd27d934d7b3a55, ; 463: SQLitePCLRaw.core => 142
	i64 u0x90263f8448b8f572, ; 464: lib_System.Diagnostics.TraceSource.dll.so => 201
	i64 u0x903101b46fb73a04, ; 465: _Microsoft.Android.Resource.Designer => 86
	i64 u0x90393bd4865292f3, ; 466: lib_System.IO.Compression.dll.so => 208
	i64 u0x90634f86c5ebe2b5, ; 467: Xamarin.AndroidX.Lifecycle.ViewModel.Android => 160
	i64 u0x907b636704ad79ef, ; 468: lib_Microsoft.Maui.Controls.Xaml.dll.so => 129
	i64 u0x90b20db1fb5b1ab3, ; 469: ja/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 31
	i64 u0x90f95fc914407a17, ; 470: lib-pl-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 20
	i64 u0x9130488ea254524d, ; 471: lib-de-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 40
	i64 u0x91418dc638b29e68, ; 472: lib_Xamarin.AndroidX.CustomView.dll.so => 155
	i64 u0x9157bd523cd7ed36, ; 473: lib_System.Text.Json.dll.so => 250
	i64 u0x91a74f07b30d37e2, ; 474: System.Linq.dll => 215
	i64 u0x91fa41a87223399f, ; 475: ca/Microsoft.Maui.Controls.resources.dll => 53
	i64 u0x926c3cf189fe2e18, ; 476: zh-Hans/Microsoft.CodeAnalysis.resources.dll => 11
	i64 u0x928614058c40c4cd, ; 477: lib_System.Xml.XPath.XDocument.dll.so => 261
	i64 u0x929cc88e0bc1eddb, ; 478: cs/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 26
	i64 u0x93ba953181e66fd2, ; 479: lib-ru-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 22
	i64 u0x93cfa73ab28d6e35, ; 480: ms/Microsoft.Maui.Controls.resources => 69
	i64 u0x944077d8ca3c6580, ; 481: System.IO.Compression.dll => 208
	i64 u0x948d746a7702861f, ; 482: Microsoft.IdentityModel.Logging.dll => 125
	i64 u0x9564283c37ed59a9, ; 483: lib_Microsoft.IdentityModel.Logging.dll.so => 125
	i64 u0x961e80b393a1fa82, ; 484: fr/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 29
	i64 u0x967fc325e09bfa8c, ; 485: es/Microsoft.Maui.Controls.resources => 58
	i64 u0x9732d8dbddea3d9a, ; 486: id/Microsoft.Maui.Controls.resources => 65
	i64 u0x978be80e5210d31b, ; 487: Microsoft.Maui.Graphics.dll => 132
	i64 u0x97b8c771ea3e4220, ; 488: System.ComponentModel.dll => 196
	i64 u0x97e144c9d3c6976e, ; 489: System.Collections.Concurrent.dll => 188
	i64 u0x98270c46908e26f7, ; 490: zh-Hant/Microsoft.CodeAnalysis.CSharp.resources.dll => 25
	i64 u0x983fad367215b4f2, ; 491: ru/Microsoft.CodeAnalysis.Scripting.resources.dll => 48
	i64 u0x98b05cc81e6f333c, ; 492: Xamarin.AndroidX.SavedState.SavedState.Android.dll => 168
	i64 u0x98e6c4091164aa39, ; 493: lib-ko-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 32
	i64 u0x991d510397f92d9d, ; 494: System.Linq.Expressions => 212
	i64 u0x999cb19e1a04ffd3, ; 495: CommunityToolkit.Mvvm.dll => 88
	i64 u0x99a891b860c3d03b, ; 496: lib-ko-Microsoft.CodeAnalysis.resources.dll.so => 6
	i64 u0x99cdc6d1f2d3a72f, ; 497: ko/Microsoft.Maui.Controls.resources.dll => 68
	i64 u0x9a102e560c6efe86, ; 498: lib-pt-BR-Microsoft.CodeAnalysis.resources.dll.so => 8
	i64 u0x9b211a749105beac, ; 499: System.Transactions.Local => 256
	i64 u0x9ba8c32873c681c1, ; 500: it/Microsoft.CodeAnalysis.CSharp.resources.dll => 17
	i64 u0x9be4124ffc84e7ee, ; 501: pl/Microsoft.CodeAnalysis.resources.dll => 7
	i64 u0x9c23027a7e62afa2, ; 502: it/Microsoft.CodeAnalysis.Scripting.resources => 43
	i64 u0x9c69fdfa9a154b28, ; 503: tr/Microsoft.CodeAnalysis.CSharp.resources.dll => 23
	i64 u0x9c8f6872beab6408, ; 504: System.Xml.XPath.XDocument.dll => 261
	i64 u0x9cfa5a38cce8589d, ; 505: AccessibleTrader.Plugins.Fred => 183
	i64 u0x9d052eb79c53b587, ; 506: lib_Polly.dll.so => 134
	i64 u0x9d5dbcf5a48583fe, ; 507: lib_Xamarin.AndroidX.Activity.dll.so => 147
	i64 u0x9d74dee1a7725f34, ; 508: Microsoft.Extensions.Configuration.Abstractions.dll => 107
	i64 u0x9db5f1062fcf5cf3, ; 509: ru/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 35
	i64 u0x9dcb570d9792d506, ; 510: lib-ru-Microsoft.CodeAnalysis.resources.dll.so => 9
	i64 u0x9dd0e195825d65c6, ; 511: lib_Xamarin.AndroidX.Navigation.Runtime.Android.dll.so => 165
	i64 u0x9e4534b6adaf6e84, ; 512: nl/Microsoft.Maui.Controls.resources => 71
	i64 u0x9e5a208afd9d15a6, ; 513: it/Microsoft.CodeAnalysis.CSharp.resources => 17
	i64 u0x9ef542cf1f78c506, ; 514: Xamarin.AndroidX.Lifecycle.LiveData.Core => 159
	i64 u0x9f4a6314df5b756a, ; 515: lib_AccessibleTrader.Plugins.Alpaca.dll.so => 179
	i64 u0x9f98edd7ed0d2e46, ; 516: ru/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 35
	i64 u0x9fbb2961ca18e5c2, ; 517: Microsoft.Extensions.FileProviders.Physical.dll => 115
	i64 u0xa0d8259f4cc284ec, ; 518: lib_System.Security.Cryptography.dll.so => 246
	i64 u0xa0e17ca50c77a225, ; 519: lib_Xamarin.Google.Crypto.Tink.Android.dll.so => 174
	i64 u0xa1440773ee9d341e, ; 520: Xamarin.Google.Android.Material => 173
	i64 u0xa1b9d7c27f47219f, ; 521: Xamarin.AndroidX.Navigation.UI.dll => 166
	i64 u0xa2572680829d2c7c, ; 522: System.IO.Pipelines.dll => 211
	i64 u0xa2beee74530fc01c, ; 523: SkiaSharp.Views.Android => 138
	i64 u0xa34c544cec85bec6, ; 524: Microsoft.CodeAnalysis.CSharp.Scripting.dll => 97
	i64 u0xa3d089b150e18d27, ; 525: pt-BR/Microsoft.CodeAnalysis.resources.dll => 8
	i64 u0xa46aa1eaa214539b, ; 526: ko/Microsoft.Maui.Controls.resources => 68
	i64 u0xa4a372eecb9e4df0, ; 527: Microsoft.Extensions.Diagnostics => 111
	i64 u0xa4d20d2ff0563d26, ; 528: lib_CommunityToolkit.Mvvm.dll.so => 88
	i64 u0xa4edc8f2ceae241a, ; 529: System.Data.Common.dll => 198
	i64 u0xa5494f40f128ce6a, ; 530: System.Runtime.Serialization.Formatters.dll => 242
	i64 u0xa5b7152421ed6d98, ; 531: lib_System.IO.FileSystem.Watcher.dll.so => 209
	i64 u0xa5c3844f17b822db, ; 532: lib_System.Linq.Parallel.dll.so => 213
	i64 u0xa5e599d1e0524750, ; 533: System.Numerics.Vectors.dll => 228
	i64 u0xa5f1ba49b85dd355, ; 534: System.Security.Cryptography.dll => 246
	i64 u0xa628f1af0ba9340a, ; 535: ko/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 32
	i64 u0xa684b098dd27b296, ; 536: lib_Xamarin.AndroidX.Security.SecurityCrypto.dll.so => 169
	i64 u0xa68a420042bb9b1f, ; 537: Xamarin.AndroidX.DrawerLayout.dll => 156
	i64 u0xa78ce3745383236a, ; 538: Xamarin.AndroidX.Lifecycle.Common.Jvm => 158
	i64 u0xa7c31b56b4dc7b33, ; 539: hu/Microsoft.Maui.Controls.resources => 64
	i64 u0xa82fd211eef00a5b, ; 540: Microsoft.Extensions.FileProviders.Physical => 115
	i64 u0xa8adea9b1f260c23, ; 541: lib-it-Microsoft.CodeAnalysis.resources.dll.so => 4
	i64 u0xa8be86a05ed1a53e, ; 542: lib_AccessibleTrader.Plugins.Fred.dll.so => 183
	i64 u0xa8e6320dd07580ef, ; 543: lib_Microsoft.IdentityModel.JsonWebTokens.dll.so => 124
	i64 u0xa9e0069186cb7a28, ; 544: Binance.Net.dll => 87
	i64 u0xaa2219c8e3449ff5, ; 545: Microsoft.Extensions.Logging.Abstractions => 120
	i64 u0xaa443ac34067eeef, ; 546: System.Private.Xml.dll => 232
	i64 u0xaa52de307ef5d1dd, ; 547: System.Net.Http => 217
	i64 u0xaa9a7b0214a5cc5c, ; 548: System.Diagnostics.StackTrace.dll => 200
	i64 u0xaaaf86367285a918, ; 549: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 109
	i64 u0xaae72bd80754669a, ; 550: lib-es-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 15
	i64 u0xaaf84bb3f052a265, ; 551: el/Microsoft.Maui.Controls.resources => 57
	i64 u0xab31ca393f222dc0, ; 552: Skender.Stock.Indicators => 136
	i64 u0xab9c1b2687d86b0b, ; 553: lib_System.Linq.Expressions.dll.so => 212
	i64 u0xac2af3fa195a15ce, ; 554: System.Runtime.Numerics => 241
	i64 u0xac5376a2a538dc10, ; 555: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 159
	i64 u0xac98d31068e24591, ; 556: System.Xml.XDocument => 260
	i64 u0xacd46e002c3ccb97, ; 557: ro/Microsoft.Maui.Controls.resources => 75
	i64 u0xacf42eea7ef9cd12, ; 558: System.Threading.Channels => 252
	i64 u0xad22fd2d199d997b, ; 559: zh-Hans/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 37
	i64 u0xad85b8f184f001b8, ; 560: AccessibleTrader.Plugins.Coinbase => 182
	i64 u0xad89c07347f1bad6, ; 561: nl/Microsoft.Maui.Controls.resources.dll => 71
	i64 u0xadbb53caf78a79d2, ; 562: System.Web.HttpUtility => 257
	i64 u0xadc90ab061a9e6e4, ; 563: System.ComponentModel.TypeConverter.dll => 195
	i64 u0xadf511667bef3595, ; 564: System.Net.Security => 222
	i64 u0xae282bcd03739de7, ; 565: Java.Interop => 265
	i64 u0xae483506cdfe2b0f, ; 566: lib-cs-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 26
	i64 u0xae53579c90db1107, ; 567: System.ObjectModel.dll => 229
	i64 u0xaeafff290ccb288d, ; 568: cs/Microsoft.CodeAnalysis.CSharp.resources => 13
	i64 u0xaf12fb8133ac3fbb, ; 569: Microsoft.EntityFrameworkCore.Sqlite => 103
	i64 u0xaf833d66e3dafcbd, ; 570: lib-it-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 30
	i64 u0xb05cc42cd94c6d9d, ; 571: lib-sv-Microsoft.Maui.Controls.resources.dll.so => 78
	i64 u0xb0bb43dc52ea59f9, ; 572: System.Diagnostics.Tracing.dll => 202
	i64 u0xb0c6678edfb08a6d, ; 573: lib-es-Microsoft.CodeAnalysis.resources.dll.so => 2
	i64 u0xb1ccbf6243328d1c, ; 574: Microsoft.AspNetCore.Components => 91
	i64 u0xb220631954820169, ; 575: System.Text.RegularExpressions => 251
	i64 u0xb2483fcbee21f661, ; 576: tr/Microsoft.CodeAnalysis.Scripting.resources => 49
	i64 u0xb2a3f67f3bf29fce, ; 577: da/Microsoft.Maui.Controls.resources => 55
	i64 u0xb36f863ae8997ccc, ; 578: lib_AccessibleTrader.BlazorClient.dll.so => 186
	i64 u0xb3d5b1cf730ea936, ; 579: pt-BR/Microsoft.CodeAnalysis.resources => 8
	i64 u0xb3f0a0fcda8d3ebc, ; 580: Xamarin.AndroidX.CardView => 150
	i64 u0xb40712daf22db873, ; 581: ko/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 32
	i64 u0xb46be1aa6d4fff93, ; 582: hi/Microsoft.Maui.Controls.resources => 62
	i64 u0xb477491be13109d8, ; 583: ar/Microsoft.Maui.Controls.resources => 52
	i64 u0xb49179af0e452f69, ; 584: zh-Hans/Microsoft.CodeAnalysis.Scripting.resources => 50
	i64 u0xb4b3092fd37a579a, ; 585: ja/Microsoft.CodeAnalysis.CSharp.resources.dll => 18
	i64 u0xb4bd7015ecee9d86, ; 586: System.IO.Pipelines => 211
	i64 u0xb5c7fcdafbc67ee4, ; 587: Microsoft.Extensions.Logging.Abstractions.dll => 120
	i64 u0xb6daa312e893d3c4, ; 588: lib-ja-Microsoft.CodeAnalysis.resources.dll.so => 5
	i64 u0xb7212c4683a94afe, ; 589: System.Drawing.Primitives => 203
	i64 u0xb7b7753d1f319409, ; 590: sv/Microsoft.Maui.Controls.resources => 78
	i64 u0xb81a2c6e0aee50fe, ; 591: lib_System.Private.CoreLib.dll.so => 264
	i64 u0xb872c26142d22aa9, ; 592: Microsoft.Extensions.Http.dll => 118
	i64 u0xb8e560c18b3fa538, ; 593: AccessibleTrader.Core => 178
	i64 u0xb9185c33a1643eed, ; 594: Microsoft.CSharp.dll => 187
	i64 u0xb960d6b2200ba320, ; 595: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll => 161
	i64 u0xb9f64d3b230def68, ; 596: lib-pt-Microsoft.Maui.Controls.resources.dll.so => 74
	i64 u0xb9fc3c8a556e3691, ; 597: ja/Microsoft.Maui.Controls.resources => 67
	i64 u0xba4670aa94a2b3c6, ; 598: lib_System.Xml.XDocument.dll.so => 260
	i64 u0xba48785529705af9, ; 599: System.Collections.dll => 192
	i64 u0xbaf762c4825c14e9, ; 600: Microsoft.AspNetCore.Components.WebView => 93
	i64 u0xbb65706fde942ce3, ; 601: System.Net.Sockets => 223
	i64 u0xbb822a624c99bd72, ; 602: lib-zh-Hans-Microsoft.CodeAnalysis.resources.dll.so => 11
	i64 u0xbbd180354b67271a, ; 603: System.Runtime.Serialization.Formatters => 242
	i64 u0xbc0ad520c3be6d31, ; 604: ja/Microsoft.CodeAnalysis.resources => 5
	i64 u0xbc22a245dab70cb4, ; 605: lib_SQLitePCLRaw.provider.e_sqlite3.dll.so => 144
	i64 u0xbd0e2c0d55246576, ; 606: System.Net.Http.dll => 217
	i64 u0xbd437a2cdb333d0d, ; 607: Xamarin.AndroidX.ViewPager2 => 172
	i64 u0xbd877b14d0b56392, ; 608: System.Runtime.Intrinsics.dll => 239
	i64 u0xbee38d4a88835966, ; 609: Xamarin.AndroidX.AppCompat.AppCompatResources => 149
	i64 u0xbfd57e7eba42c6c7, ; 610: de/Microsoft.CodeAnalysis.CSharp.resources.dll => 14
	i64 u0xc040a4ab55817f58, ; 611: ar/Microsoft.Maui.Controls.resources.dll => 52
	i64 u0xc0d928351ab5ca77, ; 612: System.Console.dll => 197
	i64 u0xc0f5a221a9383aea, ; 613: System.Runtime.Intrinsics => 239
	i64 u0xc12b8b3afa48329c, ; 614: lib_System.Linq.dll.so => 215
	i64 u0xc1afcc0a4309f4e3, ; 615: ko/Microsoft.CodeAnalysis.resources.dll => 6
	i64 u0xc1c2cb7af77b8858, ; 616: Microsoft.EntityFrameworkCore => 100
	i64 u0xc1ff9ae3cdb6e1e6, ; 617: Xamarin.AndroidX.Activity.dll => 147
	i64 u0xc278de356ad8a9e3, ; 618: Microsoft.IdentityModel.Logging => 125
	i64 u0xc28c50f32f81cc73, ; 619: ja/Microsoft.Maui.Controls.resources.dll => 67
	i64 u0xc2a3bca55b573141, ; 620: System.IO.FileSystem.Watcher => 209
	i64 u0xc2bcfec99f69365e, ; 621: Xamarin.AndroidX.ViewPager2.dll => 172
	i64 u0xc3492f8f90f96ce4, ; 622: lib_Microsoft.Extensions.DependencyModel.dll.so => 110
	i64 u0xc3e329d2e75fd52c, ; 623: lib-ru-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 48
	i64 u0xc3e74964279d65e6, ; 624: zh-Hans/Microsoft.CodeAnalysis.resources => 11
	i64 u0xc421b61fd853169d, ; 625: lib_System.Net.WebSockets.Client.dll.so => 226
	i64 u0xc439b9764edec517, ; 626: AccessibleTrader.BlazorClient.dll => 186
	i64 u0xc472ce300460ccb6, ; 627: Microsoft.EntityFrameworkCore.dll => 100
	i64 u0xc4d69851fe06342f, ; 628: lib_Microsoft.Extensions.Caching.Memory.dll.so => 105
	i64 u0xc50fded0ded1418c, ; 629: lib_System.ComponentModel.TypeConverter.dll.so => 195
	i64 u0xc519125d6bc8fb11, ; 630: lib_System.Net.Requests.dll.so => 221
	i64 u0xc5293b19e4dc230e, ; 631: Xamarin.AndroidX.Navigation.Fragment => 164
	i64 u0xc5325b2fcb37446f, ; 632: lib_System.Private.Xml.dll.so => 232
	i64 u0xc5a0f4b95a699af7, ; 633: lib_System.Private.Uri.dll.so => 230
	i64 u0xc610594226ea2d3a, ; 634: cs/Microsoft.CodeAnalysis.Scripting.resources => 39
	i64 u0xc6b898b1a52bd31f, ; 635: lib_Binance.Net.dll.so => 87
	i64 u0xc74d70d4aa96cef3, ; 636: Xamarin.AndroidX.Navigation.Runtime.Android => 165
	i64 u0xc7c01e7d7c93a110, ; 637: System.Text.Encoding.Extensions.dll => 248
	i64 u0xc7ce851898a4548e, ; 638: lib_System.Web.HttpUtility.dll.so => 257
	i64 u0xc858a28d9ee5a6c5, ; 639: lib_System.Collections.Specialized.dll.so => 191
	i64 u0xc90db8ddc202826f, ; 640: AccessibleTrader.Plugins.Polygon.dll => 184
	i64 u0xc99f841cdb0c1d8d, ; 641: AccessibleTrader.BlazorClient => 186
	i64 u0xc9b4f5dc9735cfdd, ; 642: lib-zh-Hans-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 37
	i64 u0xca3110fea81c8916, ; 643: Microsoft.AspNetCore.Components.Web.dll => 92
	i64 u0xca32340d8d54dcd5, ; 644: Microsoft.Extensions.Caching.Memory.dll => 105
	i64 u0xca3a723e7342c5b6, ; 645: lib-tr-Microsoft.Maui.Controls.resources.dll.so => 80
	i64 u0xcab3493c70141c2d, ; 646: pl/Microsoft.Maui.Controls.resources => 72
	i64 u0xcacfddc9f7c6de76, ; 647: ro/Microsoft.Maui.Controls.resources.dll => 75
	i64 u0xcb45618372c47127, ; 648: Microsoft.EntityFrameworkCore.Relational => 102
	i64 u0xcb76efab0f56f81a, ; 649: System.Reactive => 146
	i64 u0xcbd4fdd9cef4a294, ; 650: lib__Microsoft.Android.Resource.Designer.dll.so => 86
	i64 u0xcbf8c1d4c780610e, ; 651: lib_Polly.Core.dll.so => 135
	i64 u0xcc2876b32ef2794c, ; 652: lib_System.Text.RegularExpressions.dll.so => 251
	i64 u0xcc5c3bb714c4561e, ; 653: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 176
	i64 u0xcc76886e09b88260, ; 654: Xamarin.KotlinX.Serialization.Core.Jvm.dll => 177
	i64 u0xccf25c4b634ccd3a, ; 655: zh-Hans/Microsoft.Maui.Controls.resources.dll => 84
	i64 u0xcd10a42808629144, ; 656: System.Net.Requests => 221
	i64 u0xcdd0c48b6937b21c, ; 657: Xamarin.AndroidX.SwipeRefreshLayout => 170
	i64 u0xcf23d8093f3ceadf, ; 658: System.Diagnostics.DiagnosticSource.dll => 199
	i64 u0xcf8fc898f98b0d34, ; 659: System.Private.Xml.Linq => 231
	i64 u0xd013a947d55a77ab, ; 660: lib-it-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 43
	i64 u0xd04b5f59ed596e31, ; 661: System.Reflection.Metadata.dll => 235
	i64 u0xd0973385d3eb8971, ; 662: it/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 30
	i64 u0xd118cf03aa687fdf, ; 663: cs/Microsoft.CodeAnalysis.resources => 0
	i64 u0xd1194e1d8a8de83c, ; 664: lib_Xamarin.AndroidX.Lifecycle.Common.Jvm.dll.so => 158
	i64 u0xd16ec72089dd441b, ; 665: CryptoExchange.Net => 89
	i64 u0xd16fd7fb9bbcd43e, ; 666: Microsoft.Extensions.Diagnostics.Abstractions => 112
	i64 u0xd2505d8abeed6983, ; 667: lib_Microsoft.AspNetCore.Components.Web.dll.so => 92
	i64 u0xd2a56d8bff7b34be, ; 668: es/Microsoft.CodeAnalysis.Scripting.resources.dll => 41
	i64 u0xd333d0af9e423810, ; 669: System.Runtime.InteropServices => 238
	i64 u0xd3426d966bb704f5, ; 670: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 149
	i64 u0xd3651b6fc3125825, ; 671: System.Private.Uri.dll => 230
	i64 u0xd373685349b1fe8b, ; 672: Microsoft.Extensions.Logging.dll => 119
	i64 u0xd3e4c8d6a2d5d470, ; 673: it/Microsoft.Maui.Controls.resources => 66
	i64 u0xd3f33084bf9149d8, ; 674: cs/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 26
	i64 u0xd42655883bb8c19f, ; 675: Microsoft.EntityFrameworkCore.Abstractions.dll => 101
	i64 u0xd4645626dffec99d, ; 676: lib_Microsoft.Extensions.DependencyInjection.Abstractions.dll.so => 109
	i64 u0xd46b4a8758d1f3ee, ; 677: Microsoft.Extensions.FileProviders.Composite.dll => 114
	i64 u0xd5c37cd77ea86c02, ; 678: Microsoft.CodeAnalysis.CSharp.Scripting => 97
	i64 u0xd6d21782156bc35b, ; 679: Xamarin.AndroidX.SwipeRefreshLayout.dll => 170
	i64 u0xd6e2b34595283a1a, ; 680: lib-es-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 41
	i64 u0xd72329819cbbbc44, ; 681: lib_Microsoft.Extensions.Configuration.Abstractions.dll.so => 107
	i64 u0xd7b3764ada9d341d, ; 682: lib_Microsoft.Extensions.Logging.Abstractions.dll.so => 120
	i64 u0xda1dfa4c534a9251, ; 683: Microsoft.Extensions.DependencyInjection => 108
	i64 u0xdad05a11827959a3, ; 684: System.Collections.NonGeneric.dll => 190
	i64 u0xdb5383ab5865c007, ; 685: lib-vi-Microsoft.Maui.Controls.resources.dll.so => 82
	i64 u0xdb58816721c02a59, ; 686: lib_System.Reflection.Emit.ILGeneration.dll.so => 233
	i64 u0xdb8f858873e2186b, ; 687: SkiaSharp.Views.Maui.Controls => 139
	i64 u0xdbeda89f832aa805, ; 688: vi/Microsoft.Maui.Controls.resources.dll => 82
	i64 u0xdbf2a779fbc3ac31, ; 689: System.Transactions.Local.dll => 256
	i64 u0xdbf9607a441b4505, ; 690: System.Linq => 215
	i64 u0xdc75032002d1a212, ; 691: lib_System.Transactions.Local.dll.so => 256
	i64 u0xdca8be7403f92d4f, ; 692: lib_System.Linq.Queryable.dll.so => 214
	i64 u0xdcbf1e32b739302e, ; 693: de/Microsoft.CodeAnalysis.resources => 1
	i64 u0xdccc74da2720e84e, ; 694: lib-ja-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 31
	i64 u0xdce2c53525640bf3, ; 695: Microsoft.Extensions.Logging => 119
	i64 u0xdcf0ce03827b19b2, ; 696: lib_AccessibleTrader.Plugins.Binance.dll.so => 180
	i64 u0xdcfac8ea6983b7d2, ; 697: lib_DynamicData.dll.so => 90
	i64 u0xdd14049e4243731e, ; 698: lib-it-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 17
	i64 u0xdd2b722d78ef5f43, ; 699: System.Runtime.dll => 244
	i64 u0xdd67031857c72f96, ; 700: lib_System.Text.Encodings.Web.dll.so => 249
	i64 u0xdde30e6b77aa6f6c, ; 701: lib-zh-Hans-Microsoft.Maui.Controls.resources.dll.so => 84
	i64 u0xde110ae80fa7c2e2, ; 702: System.Xml.XDocument.dll => 260
	i64 u0xde8769ebda7d8647, ; 703: hr/Microsoft.Maui.Controls.resources.dll => 63
	i64 u0xdef7031c225c4fa7, ; 704: lib_AccessibleTrader.Core.dll.so => 178
	i64 u0xe0142572c095a480, ; 705: Xamarin.AndroidX.AppCompat.dll => 148
	i64 u0xe02f89350ec78051, ; 706: Xamarin.AndroidX.CoordinatorLayout.dll => 152
	i64 u0xe0c6976bbf521a2b, ; 707: AccessibleTrader.Plugins.Alpaca.dll => 179
	i64 u0xe192a588d4410686, ; 708: lib_System.IO.Pipelines.dll.so => 211
	i64 u0xe1a08bd3fa539e0d, ; 709: System.Runtime.Loader => 240
	i64 u0xe1b52f9f816c70ef, ; 710: System.Private.Xml.Linq.dll => 231
	i64 u0xe1e852de9692e4b8, ; 711: es/Microsoft.CodeAnalysis.CSharp.resources => 15
	i64 u0xe1ecfdb7fff86067, ; 712: System.Net.Security.dll => 222
	i64 u0xe24095a7afddaab3, ; 713: lib_Microsoft.Extensions.Hosting.Abstractions.dll.so => 117
	i64 u0xe2420585aeceb728, ; 714: System.Net.Requests.dll => 221
	i64 u0xe29b73bc11392966, ; 715: lib-id-Microsoft.Maui.Controls.resources.dll.so => 65
	i64 u0xe31089e70e4e84ee, ; 716: Microsoft.AspNetCore.Components.WebView.Maui => 94
	i64 u0xe3811d68d4fe8463, ; 717: pt-BR/Microsoft.Maui.Controls.resources.dll => 73
	i64 u0xe3e8c409bce3f768, ; 718: AccessibleTrader.Plugins.Binance.dll => 180
	i64 u0xe494f7ced4ecd10a, ; 719: hu/Microsoft.Maui.Controls.resources.dll => 64
	i64 u0xe4a9b1e40d1e8917, ; 720: lib-fi-Microsoft.Maui.Controls.resources.dll.so => 59
	i64 u0xe4f74a0b5bf9703f, ; 721: System.Runtime.Serialization.Primitives => 243
	i64 u0xe51aadb833ed7eb1, ; 722: lib_Microsoft.CodeAnalysis.CSharp.dll.so => 96
	i64 u0xe529964b351f8a52, ; 723: pt-BR/Microsoft.CodeAnalysis.CSharp.resources.dll => 21
	i64 u0xe5434e8a119ceb69, ; 724: lib_Mono.Android.dll.so => 267
	i64 u0xe5a1ab00af921efe, ; 725: lib_AccessibleTrader.Sdk.dll.so => 185
	i64 u0xe6e66793cecd1487, ; 726: lib_AccessibleTrader.Plugins.Polygon.dll.so => 184
	i64 u0xe7b916eaefda3b00, ; 727: fr/Microsoft.CodeAnalysis.resources.dll => 3
	i64 u0xe7dd1e7ea292e8bc, ; 728: ko/Microsoft.CodeAnalysis.resources => 6
	i64 u0xe7e03cc18dcdeb49, ; 729: lib_System.Diagnostics.StackTrace.dll.so => 200
	i64 u0xe865c771a4011ece, ; 730: de/Microsoft.CodeAnalysis.Scripting.resources => 40
	i64 u0xe89a2a9ef110899b, ; 731: System.Drawing.dll => 204
	i64 u0xe9772100456fb4b4, ; 732: Microsoft.AspNetCore.Components.dll => 91
	i64 u0xea9da4f091bb0d3f, ; 733: lib-tr-Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll.so => 36
	i64 u0xeb322ae59a10b855, ; 734: AccessibleTrader.Sdk => 185
	i64 u0xeb692e0662456c7c, ; 735: lib_AccessibleTrader.Plugins.Coinbase.dll.so => 182
	i64 u0xecd614b0c28f4cf0, ; 736: pl/Microsoft.CodeAnalysis.Scripting.resources.dll => 46
	i64 u0xedc4817167106c23, ; 737: System.Net.Sockets.dll => 223
	i64 u0xedc632067fb20ff3, ; 738: System.Memory.dll => 216
	i64 u0xee4c2ef307cf87b1, ; 739: cs/Microsoft.CodeAnalysis.Scripting.resources.dll => 39
	i64 u0xeeb7ebb80150501b, ; 740: lib_Xamarin.AndroidX.Collection.Jvm.dll.so => 151
	i64 u0xeed0f6520e39d6af, ; 741: pt-BR/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 34
	i64 u0xef03b1b5a04e9709, ; 742: System.Text.Encoding.CodePages.dll => 247
	i64 u0xef72742e1bcca27a, ; 743: Microsoft.Maui.Essentials.dll => 131
	i64 u0xefec0b7fdc57ec42, ; 744: Xamarin.AndroidX.Activity => 147
	i64 u0xf00c29406ea45e19, ; 745: es/Microsoft.Maui.Controls.resources.dll => 58
	i64 u0xf09e47b6ae914f6e, ; 746: System.Net.NameResolution => 218
	i64 u0xf0b176644a6e46bd, ; 747: fr/Microsoft.CodeAnalysis.Scripting.resources.dll => 42
	i64 u0xf0de2537ee19c6ca, ; 748: lib_System.Net.WebHeaderCollection.dll.so => 224
	i64 u0xf11b621fc87b983f, ; 749: Microsoft.Maui.Controls.Xaml.dll => 129
	i64 u0xf1c4b4005493d871, ; 750: System.Formats.Asn1.dll => 205
	i64 u0xf22514cfad2d598b, ; 751: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll.so => 161
	i64 u0xf238bd79489d3a96, ; 752: lib-nl-Microsoft.Maui.Controls.resources.dll.so => 71
	i64 u0xf27ac96c4a2c11ce, ; 753: lib-fr-Microsoft.CodeAnalysis.resources.dll.so => 3
	i64 u0xf37221fda4ef8830, ; 754: lib_Xamarin.Google.Android.Material.dll.so => 173
	i64 u0xf3ddfe05336abf29, ; 755: System => 262
	i64 u0xf408654b2a135055, ; 756: System.Reflection.Emit.ILGeneration.dll => 233
	i64 u0xf4103170a1de5bd0, ; 757: System.Linq.Queryable.dll => 214
	i64 u0xf41b241c82f75cde, ; 758: ru/Microsoft.CodeAnalysis.CSharp.resources.dll => 22
	i64 u0xf41eebf9fb91e2a1, ; 759: it/Microsoft.CodeAnalysis.resources.dll => 4
	i64 u0xf42b5c4d56f6a043, ; 760: tr/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 36
	i64 u0xf4727d423e5d26f3, ; 761: SkiaSharp => 137
	i64 u0xf4c1dd70a5496a17, ; 762: System.IO.Compression => 208
	i64 u0xf4d5dd0c159319cd, ; 763: pl/Microsoft.CodeAnalysis.CSharp.Scripting.resources.dll => 33
	i64 u0xf5967aac376787d7, ; 764: Microsoft.CodeAnalysis.dll => 95
	i64 u0xf5fc7602fe27b333, ; 765: System.Net.WebHeaderCollection => 224
	i64 u0xf6077741019d7428, ; 766: Xamarin.AndroidX.CoordinatorLayout => 152
	i64 u0xf61ade9836ad4692, ; 767: Microsoft.IdentityModel.Tokens.dll => 126
	i64 u0xf6c0e7d55a7a4e4f, ; 768: Microsoft.IdentityModel.JsonWebTokens => 124
	i64 u0xf77b20923f07c667, ; 769: de/Microsoft.Maui.Controls.resources.dll => 56
	i64 u0xf785c93afc3b7acc, ; 770: lib-ko-Microsoft.CodeAnalysis.Scripting.resources.dll.so => 45
	i64 u0xf7e2cac4c45067b3, ; 771: lib_System.Numerics.Vectors.dll.so => 228
	i64 u0xf7e74930e0e3d214, ; 772: zh-HK/Microsoft.Maui.Controls.resources.dll => 83
	i64 u0xf7fa0bf77fe677cc, ; 773: Newtonsoft.Json.dll => 133
	i64 u0xf84773b5c81e3cef, ; 774: lib-uk-Microsoft.Maui.Controls.resources.dll.so => 81
	i64 u0xf8aac5ea82de1348, ; 775: System.Linq.Queryable => 214
	i64 u0xf8b77539b362d3ba, ; 776: lib_System.Reflection.Primitives.dll.so => 236
	i64 u0xf8e045dc345b2ea3, ; 777: lib_Xamarin.AndroidX.RecyclerView.dll.so => 167
	i64 u0xf915dc29808193a1, ; 778: System.Web.HttpUtility.dll => 257
	i64 u0xf96c777a2a0686f4, ; 779: hi/Microsoft.Maui.Controls.resources.dll => 62
	i64 u0xf9d1b36df657e81b, ; 780: AccessibleTrader.Core.dll => 178
	i64 u0xf9eec5bb3a6aedc6, ; 781: Microsoft.Extensions.Options => 121
	i64 u0xfa3f278f288b0e84, ; 782: lib_System.Net.Security.dll.so => 222
	i64 u0xfa504dfa0f097d72, ; 783: Microsoft.Extensions.FileProviders.Abstractions.dll => 113
	i64 u0xfa5ed7226d978949, ; 784: lib-ar-Microsoft.Maui.Controls.resources.dll.so => 52
	i64 u0xfa5eec7bc90bf0a5, ; 785: de/Microsoft.CodeAnalysis.Scripting.resources.dll => 40
	i64 u0xfa645d91e9fc4cba, ; 786: System.Threading.Thread => 254
	i64 u0xfa99d44ebf9bea5b, ; 787: SkiaSharp.Views.Maui.Core => 140
	i64 u0xfad7aabd979f6593, ; 788: Skender.Stock.Indicators.dll => 136
	i64 u0xfb022853d73b7fa5, ; 789: lib_SQLitePCLRaw.batteries_v2.dll.so => 141
	i64 u0xfbf0a31c9fc34bc4, ; 790: lib_System.Net.Http.dll.so => 217
	i64 u0xfc6b7527cc280b3f, ; 791: lib_System.Runtime.Serialization.Formatters.dll.so => 242
	i64 u0xfc719aec26adf9d9, ; 792: Xamarin.AndroidX.Navigation.Fragment.dll => 164
	i64 u0xfcd302092ada6328, ; 793: System.IO.MemoryMappedFiles.dll => 210
	i64 u0xfd22f00870e40ae0, ; 794: lib_Xamarin.AndroidX.DrawerLayout.dll.so => 156
	i64 u0xfd2e866c678cac90, ; 795: lib_Microsoft.AspNetCore.Components.WebView.Maui.dll.so => 94
	i64 u0xfd49b3c1a76e2748, ; 796: System.Runtime.InteropServices.RuntimeInformation => 237
	i64 u0xfd536c702f64dc47, ; 797: System.Text.Encoding.Extensions => 248
	i64 u0xfd583f7657b6a1cb, ; 798: Xamarin.AndroidX.Fragment => 157
	i64 u0xfda36abccf05cf5c, ; 799: System.Net.WebSockets.Client => 226
	i64 u0xfeae9952cf03b8cb, ; 800: tr/Microsoft.Maui.Controls.resources => 80
	i64 u0xfec8e01187d0178c, ; 801: lib-ja-Microsoft.CodeAnalysis.CSharp.resources.dll.so => 18
	i64 u0xfec9a8625385376c, ; 802: pt-BR/Microsoft.CodeAnalysis.Scripting.resources.dll => 47
	i64 u0xffcfc81f609b0813 ; 803: zh-Hant/Microsoft.CodeAnalysis.CSharp.Scripting.resources => 38
], align 16

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [804 x i32] [
	i32 206, i32 39, i32 170, i32 193, i32 105, i32 266, i32 181, i32 148,
	i32 144, i32 76, i32 54, i32 82, i32 123, i32 220, i32 167, i32 99,
	i32 192, i32 130, i32 99, i32 83, i32 258, i32 151, i32 91, i32 76,
	i32 190, i32 236, i32 156, i32 193, i32 121, i32 190, i32 27, i32 246,
	i32 235, i32 104, i32 252, i32 77, i32 177, i32 171, i32 73, i32 267,
	i32 131, i32 169, i32 12, i32 136, i32 218, i32 4, i32 138, i32 1,
	i32 155, i32 95, i32 95, i32 27, i32 207, i32 22, i32 51, i32 227,
	i32 19, i32 234, i32 167, i32 31, i32 14, i32 12, i32 48, i32 60,
	i32 265, i32 61, i32 109, i32 19, i32 227, i32 23, i32 51, i32 110,
	i32 263, i32 64, i32 49, i32 249, i32 135, i32 177, i32 13, i32 70,
	i32 12, i32 245, i32 5, i32 188, i32 15, i32 262, i32 79, i32 7,
	i32 111, i32 115, i32 266, i32 29, i32 169, i32 200, i32 166, i32 68,
	i32 253, i32 121, i32 33, i32 46, i32 207, i32 206, i32 244, i32 27,
	i32 233, i32 79, i32 254, i32 100, i32 197, i32 153, i32 9, i32 243,
	i32 60, i32 174, i32 28, i32 141, i32 30, i32 175, i32 122, i32 145,
	i32 10, i32 65, i32 63, i32 227, i32 174, i32 265, i32 220, i32 112,
	i32 137, i32 163, i32 81, i32 28, i32 219, i32 201, i32 50, i32 59,
	i32 251, i32 145, i32 205, i32 85, i32 116, i32 89, i32 72, i32 181,
	i32 247, i32 234, i32 231, i32 255, i32 44, i32 78, i32 250, i32 42,
	i32 34, i32 44, i32 7, i32 57, i32 127, i32 259, i32 202, i32 104,
	i32 118, i32 157, i32 123, i32 86, i32 135, i32 150, i32 203, i32 0,
	i32 139, i32 60, i32 259, i32 116, i32 189, i32 58, i32 223, i32 41,
	i32 114, i32 130, i32 54, i32 20, i32 128, i32 193, i32 172, i32 106,
	i32 239, i32 16, i32 234, i32 210, i32 236, i32 189, i32 155, i32 218,
	i32 171, i32 146, i32 53, i32 134, i32 146, i32 133, i32 113, i32 248,
	i32 127, i32 168, i32 126, i32 45, i32 175, i32 258, i32 153, i32 141,
	i32 93, i32 117, i32 140, i32 2, i32 98, i32 149, i32 185, i32 263,
	i32 267, i32 72, i32 35, i32 182, i32 187, i32 243, i32 175, i32 101,
	i32 145, i32 201, i32 110, i32 76, i32 258, i32 25, i32 49, i32 74,
	i32 210, i32 229, i32 3, i32 166, i32 38, i32 24, i32 14, i32 250,
	i32 138, i32 161, i32 102, i32 162, i32 219, i32 226, i32 212, i32 232,
	i32 240, i32 66, i32 162, i32 127, i32 266, i32 247, i32 13, i32 252,
	i32 53, i32 143, i32 126, i32 128, i32 213, i32 88, i32 160, i32 225,
	i32 204, i32 220, i32 38, i32 198, i32 153, i32 132, i32 19, i32 77,
	i32 179, i32 219, i32 237, i32 83, i32 245, i32 244, i32 163, i32 16,
	i32 158, i32 191, i32 140, i32 134, i32 230, i32 21, i32 184, i32 142,
	i32 264, i32 117, i32 199, i32 29, i32 67, i32 108, i32 16, i32 187,
	i32 152, i32 255, i32 181, i32 196, i32 51, i32 139, i32 55, i32 2,
	i32 23, i32 119, i32 10, i32 224, i32 94, i32 253, i32 238, i32 151,
	i32 191, i32 28, i32 249, i32 194, i32 259, i32 198, i32 143, i32 57,
	i32 108, i32 176, i32 216, i32 129, i32 56, i32 240, i32 264, i32 50,
	i32 180, i32 165, i32 189, i32 173, i32 44, i32 9, i32 128, i32 241,
	i32 197, i32 160, i32 154, i32 55, i32 203, i32 205, i32 61, i32 143,
	i32 20, i32 238, i32 96, i32 70, i32 43, i32 45, i32 132, i32 122,
	i32 225, i32 225, i32 154, i32 122, i32 34, i32 164, i32 25, i32 33,
	i32 90, i32 130, i32 54, i32 209, i32 18, i32 80, i32 70, i32 112,
	i32 36, i32 66, i32 194, i32 63, i32 261, i32 216, i32 103, i32 42,
	i32 111, i32 106, i32 241, i32 213, i32 69, i32 79, i32 10, i32 157,
	i32 98, i32 98, i32 47, i32 96, i32 90, i32 118, i32 93, i32 59,
	i32 99, i32 124, i32 195, i32 77, i32 56, i32 163, i32 87, i32 69,
	i32 228, i32 192, i32 202, i32 245, i32 183, i32 47, i32 229, i32 196,
	i32 171, i32 46, i32 107, i32 159, i32 235, i32 24, i32 144, i32 92,
	i32 262, i32 97, i32 85, i32 148, i32 150, i32 204, i32 81, i32 123,
	i32 84, i32 114, i32 253, i32 102, i32 137, i32 37, i32 85, i32 106,
	i32 116, i32 101, i32 254, i32 207, i32 131, i32 1, i32 176, i32 263,
	i32 194, i32 168, i32 237, i32 104, i32 162, i32 89, i32 199, i32 61,
	i32 21, i32 113, i32 154, i32 103, i32 255, i32 188, i32 133, i32 62,
	i32 75, i32 142, i32 74, i32 73, i32 0, i32 24, i32 206, i32 142,
	i32 201, i32 86, i32 208, i32 160, i32 129, i32 31, i32 20, i32 40,
	i32 155, i32 250, i32 215, i32 53, i32 11, i32 261, i32 26, i32 22,
	i32 69, i32 208, i32 125, i32 125, i32 29, i32 58, i32 65, i32 132,
	i32 196, i32 188, i32 25, i32 48, i32 168, i32 32, i32 212, i32 88,
	i32 6, i32 68, i32 8, i32 256, i32 17, i32 7, i32 43, i32 23,
	i32 261, i32 183, i32 134, i32 147, i32 107, i32 35, i32 9, i32 165,
	i32 71, i32 17, i32 159, i32 179, i32 35, i32 115, i32 246, i32 174,
	i32 173, i32 166, i32 211, i32 138, i32 97, i32 8, i32 68, i32 111,
	i32 88, i32 198, i32 242, i32 209, i32 213, i32 228, i32 246, i32 32,
	i32 169, i32 156, i32 158, i32 64, i32 115, i32 4, i32 183, i32 124,
	i32 87, i32 120, i32 232, i32 217, i32 200, i32 109, i32 15, i32 57,
	i32 136, i32 212, i32 241, i32 159, i32 260, i32 75, i32 252, i32 37,
	i32 182, i32 71, i32 257, i32 195, i32 222, i32 265, i32 26, i32 229,
	i32 13, i32 103, i32 30, i32 78, i32 202, i32 2, i32 91, i32 251,
	i32 49, i32 55, i32 186, i32 8, i32 150, i32 32, i32 62, i32 52,
	i32 50, i32 18, i32 211, i32 120, i32 5, i32 203, i32 78, i32 264,
	i32 118, i32 178, i32 187, i32 161, i32 74, i32 67, i32 260, i32 192,
	i32 93, i32 223, i32 11, i32 242, i32 5, i32 144, i32 217, i32 172,
	i32 239, i32 149, i32 14, i32 52, i32 197, i32 239, i32 215, i32 6,
	i32 100, i32 147, i32 125, i32 67, i32 209, i32 172, i32 110, i32 48,
	i32 11, i32 226, i32 186, i32 100, i32 105, i32 195, i32 221, i32 164,
	i32 232, i32 230, i32 39, i32 87, i32 165, i32 248, i32 257, i32 191,
	i32 184, i32 186, i32 37, i32 92, i32 105, i32 80, i32 72, i32 75,
	i32 102, i32 146, i32 86, i32 135, i32 251, i32 176, i32 177, i32 84,
	i32 221, i32 170, i32 199, i32 231, i32 43, i32 235, i32 30, i32 0,
	i32 158, i32 89, i32 112, i32 92, i32 41, i32 238, i32 149, i32 230,
	i32 119, i32 66, i32 26, i32 101, i32 109, i32 114, i32 97, i32 170,
	i32 41, i32 107, i32 120, i32 108, i32 190, i32 82, i32 233, i32 139,
	i32 82, i32 256, i32 215, i32 256, i32 214, i32 1, i32 31, i32 119,
	i32 180, i32 90, i32 17, i32 244, i32 249, i32 84, i32 260, i32 63,
	i32 178, i32 148, i32 152, i32 179, i32 211, i32 240, i32 231, i32 15,
	i32 222, i32 117, i32 221, i32 65, i32 94, i32 73, i32 180, i32 64,
	i32 59, i32 243, i32 96, i32 21, i32 267, i32 185, i32 184, i32 3,
	i32 6, i32 200, i32 40, i32 204, i32 91, i32 36, i32 185, i32 182,
	i32 46, i32 223, i32 216, i32 39, i32 151, i32 34, i32 247, i32 131,
	i32 147, i32 58, i32 218, i32 42, i32 224, i32 129, i32 205, i32 161,
	i32 71, i32 3, i32 173, i32 262, i32 233, i32 214, i32 22, i32 4,
	i32 36, i32 137, i32 208, i32 33, i32 95, i32 224, i32 152, i32 126,
	i32 124, i32 56, i32 45, i32 228, i32 83, i32 133, i32 81, i32 214,
	i32 236, i32 167, i32 257, i32 62, i32 178, i32 121, i32 222, i32 113,
	i32 52, i32 40, i32 254, i32 140, i32 136, i32 141, i32 217, i32 242,
	i32 164, i32 210, i32 156, i32 94, i32 237, i32 248, i32 157, i32 226,
	i32 80, i32 18, i32 47, i32 38
], align 16

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
@.mm.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 16

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" "target-cpu"="x86-64" "target-features"="+crc32,+cx16,+cx8,+fxsr,+mmx,+popcnt,+sse,+sse2,+sse3,+sse4.1,+sse4.2,+ssse3,+x87" "tune-cpu"="generic" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8" "target-cpu"="x86-64" "target-features"="+crc32,+cx16,+cx8,+fxsr,+mmx,+popcnt,+sse,+sse2,+sse3,+sse4.1,+sse4.2,+ssse3,+x87" "tune-cpu"="generic" }

; Metadata
!llvm.module.flags = !{!0, !1}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!".NET for Android remotes/origin/release/10.0.1xx @ 9a2d211ba972d3a0c4c108e043def432f3ec2620"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
