; ModuleID = 'compressed_assemblies.x86_64.ll'
source_filename = "compressed_assemblies.x86_64.ll"
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-unknown-linux-android21"

%struct.CompressedAssemblyDescriptor = type {
	i32, ; uint32_t uncompressed_file_size
	i1, ; bool loaded
	i32 ; uint32_t buffer_offset
}

@compressed_assembly_count = dso_local local_unnamed_addr constant i32 268, align 4

@compressed_assembly_descriptors = dso_local local_unnamed_addr global [268 x %struct.CompressedAssemblyDescriptor] [
	%struct.CompressedAssemblyDescriptor {
		i32 40200, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 0; uint32_t buffer_offset
	}, ; 0: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40200; uint32_t buffer_offset
	}, ; 1: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41272, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 81936; uint32_t buffer_offset
	}, ; 2: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 123208; uint32_t buffer_offset
	}, ; 3: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41784, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 164944; uint32_t buffer_offset
	}, ; 4: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 44304, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 206728; uint32_t buffer_offset
	}, ; 5: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41744, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 251032; uint32_t buffer_offset
	}, ; 6: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 41736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 292776; uint32_t buffer_offset
	}, ; 7: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 40720, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 334512; uint32_t buffer_offset
	}, ; 8: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 49464, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 375232; uint32_t buffer_offset
	}, ; 9: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 40240, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 424696; uint32_t buffer_offset
	}, ; 10: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 37640, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 464936; uint32_t buffer_offset
	}, ; 11: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 38152, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 502576; uint32_t buffer_offset
	}, ; 12: Microsoft.CodeAnalysis.resources
	%struct.CompressedAssemblyDescriptor {
		i32 444168, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 540728; uint32_t buffer_offset
	}, ; 13: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 474376, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 984896; uint32_t buffer_offset
	}, ; 14: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 463120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 1459272; uint32_t buffer_offset
	}, ; 15: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 475920, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 1922392; uint32_t buffer_offset
	}, ; 16: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 469768, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 2398312; uint32_t buffer_offset
	}, ; 17: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 515848, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 2868080; uint32_t buffer_offset
	}, ; 18: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 475920, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 3383928; uint32_t buffer_offset
	}, ; 19: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 476432, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 3859848; uint32_t buffer_offset
	}, ; 20: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 456456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 4336280; uint32_t buffer_offset
	}, ; 21: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 617744, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 4792736; uint32_t buffer_offset
	}, ; 22: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 452360, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 5410480; uint32_t buffer_offset
	}, ; 23: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 405776, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 5862840; uint32_t buffer_offset
	}, ; 24: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 405304, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6268616; uint32_t buffer_offset
	}, ; 25: Microsoft.CodeAnalysis.CSharp.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17160, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6673920; uint32_t buffer_offset
	}, ; 26: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17712, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6691080; uint32_t buffer_offset
	}, ; 27: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17712, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6708792; uint32_t buffer_offset
	}, ; 28: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17160, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6726504; uint32_t buffer_offset
	}, ; 29: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17160, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6743664; uint32_t buffer_offset
	}, ; 30: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17712, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6760824; uint32_t buffer_offset
	}, ; 31: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17168, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6778536; uint32_t buffer_offset
	}, ; 32: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17208, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6795704; uint32_t buffer_offset
	}, ; 33: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17160, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6812912; uint32_t buffer_offset
	}, ; 34: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18192, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6830072; uint32_t buffer_offset
	}, ; 35: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17680, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6848264; uint32_t buffer_offset
	}, ; 36: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17160, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6865944; uint32_t buffer_offset
	}, ; 37: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 17168, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6883104; uint32_t buffer_offset
	}, ; 38: Microsoft.CodeAnalysis.CSharp.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6900272; uint32_t buffer_offset
	}, ; 39: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18744, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6918456; uint32_t buffer_offset
	}, ; 40: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6937200; uint32_t buffer_offset
	}, ; 41: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6955384; uint32_t buffer_offset
	}, ; 42: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18744, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6974080; uint32_t buffer_offset
	}, ; 43: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 6992824; uint32_t buffer_offset
	}, ; 44: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18744, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7011520; uint32_t buffer_offset
	}, ; 45: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7030264; uint32_t buffer_offset
	}, ; 46: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7048960; uint32_t buffer_offset
	}, ; 47: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 19728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7067144; uint32_t buffer_offset
	}, ; 48: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18224, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7086872; uint32_t buffer_offset
	}, ; 49: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18192, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7105096; uint32_t buffer_offset
	}, ; 50: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 18184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7123288; uint32_t buffer_offset
	}, ; 51: Microsoft.CodeAnalysis.Scripting.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7141472; uint32_t buffer_offset
	}, ; 52: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7157096; uint32_t buffer_offset
	}, ; 53: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7172728; uint32_t buffer_offset
	}, ; 54: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7188352; uint32_t buffer_offset
	}, ; 55: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7203976; uint32_t buffer_offset
	}, ; 56: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7219608; uint32_t buffer_offset
	}, ; 57: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7235240; uint32_t buffer_offset
	}, ; 58: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7250872; uint32_t buffer_offset
	}, ; 59: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7266496; uint32_t buffer_offset
	}, ; 60: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7282120; uint32_t buffer_offset
	}, ; 61: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7297752; uint32_t buffer_offset
	}, ; 62: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7313376; uint32_t buffer_offset
	}, ; 63: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7329000; uint32_t buffer_offset
	}, ; 64: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7344624; uint32_t buffer_offset
	}, ; 65: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7360248; uint32_t buffer_offset
	}, ; 66: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7375872; uint32_t buffer_offset
	}, ; 67: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7391496; uint32_t buffer_offset
	}, ; 68: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7407120; uint32_t buffer_offset
	}, ; 69: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7422744; uint32_t buffer_offset
	}, ; 70: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15664, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7438376; uint32_t buffer_offset
	}, ; 71: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7454040; uint32_t buffer_offset
	}, ; 72: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7469664; uint32_t buffer_offset
	}, ; 73: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7485296; uint32_t buffer_offset
	}, ; 74: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7500928; uint32_t buffer_offset
	}, ; 75: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15672, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7516560; uint32_t buffer_offset
	}, ; 76: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7532232; uint32_t buffer_offset
	}, ; 77: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15664, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7547864; uint32_t buffer_offset
	}, ; 78: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7563528; uint32_t buffer_offset
	}, ; 79: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7579152; uint32_t buffer_offset
	}, ; 80: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7594776; uint32_t buffer_offset
	}, ; 81: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7610400; uint32_t buffer_offset
	}, ; 82: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15664, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7626024; uint32_t buffer_offset
	}, ; 83: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7641688; uint32_t buffer_offset
	}, ; 84: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 15632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7657312; uint32_t buffer_offset
	}, ; 85: Microsoft.Maui.Controls.resources
	%struct.CompressedAssemblyDescriptor {
		i32 6144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7672944; uint32_t buffer_offset
	}, ; 86: _Microsoft.Android.Resource.Designer
	%struct.CompressedAssemblyDescriptor {
		i32 1626624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 7679088; uint32_t buffer_offset
	}, ; 87: Binance.Net
	%struct.CompressedAssemblyDescriptor {
		i32 10240, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 9305712; uint32_t buffer_offset
	}, ; 88: CommunityToolkit.Mvvm
	%struct.CompressedAssemblyDescriptor {
		i32 282112, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 9315952; uint32_t buffer_offset
	}, ; 89: CryptoExchange.Net
	%struct.CompressedAssemblyDescriptor {
		i32 969216, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 9598064; uint32_t buffer_offset
	}, ; 90: DynamicData
	%struct.CompressedAssemblyDescriptor {
		i32 244736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 10567280; uint32_t buffer_offset
	}, ; 91: Microsoft.AspNetCore.Components
	%struct.CompressedAssemblyDescriptor {
		i32 52736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 10812016; uint32_t buffer_offset
	}, ; 92: Microsoft.AspNetCore.Components.Web
	%struct.CompressedAssemblyDescriptor {
		i32 114448, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 10864752; uint32_t buffer_offset
	}, ; 93: Microsoft.AspNetCore.Components.WebView
	%struct.CompressedAssemblyDescriptor {
		i32 70456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 10979200; uint32_t buffer_offset
	}, ; 94: Microsoft.AspNetCore.Components.WebView.Maui
	%struct.CompressedAssemblyDescriptor {
		i32 3059000, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 11049656; uint32_t buffer_offset
	}, ; 95: Microsoft.CodeAnalysis
	%struct.CompressedAssemblyDescriptor {
		i32 6839608, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 14108656; uint32_t buffer_offset
	}, ; 96: Microsoft.CodeAnalysis.CSharp
	%struct.CompressedAssemblyDescriptor {
		i32 34568, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 20948264; uint32_t buffer_offset
	}, ; 97: Microsoft.CodeAnalysis.CSharp.Scripting
	%struct.CompressedAssemblyDescriptor {
		i32 138000, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 20982832; uint32_t buffer_offset
	}, ; 98: Microsoft.CodeAnalysis.Scripting
	%struct.CompressedAssemblyDescriptor {
		i32 177736, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 21120832; uint32_t buffer_offset
	}, ; 99: Microsoft.Data.Sqlite
	%struct.CompressedAssemblyDescriptor {
		i32 2825800, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 21298568; uint32_t buffer_offset
	}, ; 100: Microsoft.EntityFrameworkCore
	%struct.CompressedAssemblyDescriptor {
		i32 16896, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 24124368; uint32_t buffer_offset
	}, ; 101: Microsoft.EntityFrameworkCore.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 2193992, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 24141264; uint32_t buffer_offset
	}, ; 102: Microsoft.EntityFrameworkCore.Relational
	%struct.CompressedAssemblyDescriptor {
		i32 311880, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26335256; uint32_t buffer_offset
	}, ; 103: Microsoft.EntityFrameworkCore.Sqlite
	%struct.CompressedAssemblyDescriptor {
		i32 10752, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26647136; uint32_t buffer_offset
	}, ; 104: Microsoft.Extensions.Caching.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 25600, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26657888; uint32_t buffer_offset
	}, ; 105: Microsoft.Extensions.Caching.Memory
	%struct.CompressedAssemblyDescriptor {
		i32 15872, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26683488; uint32_t buffer_offset
	}, ; 106: Microsoft.Extensions.Configuration
	%struct.CompressedAssemblyDescriptor {
		i32 6656, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26699360; uint32_t buffer_offset
	}, ; 107: Microsoft.Extensions.Configuration.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 47104, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26706016; uint32_t buffer_offset
	}, ; 108: Microsoft.Extensions.DependencyInjection
	%struct.CompressedAssemblyDescriptor {
		i32 33792, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26753120; uint32_t buffer_offset
	}, ; 109: Microsoft.Extensions.DependencyInjection.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 35840, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26786912; uint32_t buffer_offset
	}, ; 110: Microsoft.Extensions.DependencyModel
	%struct.CompressedAssemblyDescriptor {
		i32 15360, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26822752; uint32_t buffer_offset
	}, ; 111: Microsoft.Extensions.Diagnostics
	%struct.CompressedAssemblyDescriptor {
		i32 8704, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26838112; uint32_t buffer_offset
	}, ; 112: Microsoft.Extensions.Diagnostics.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 9216, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26846816; uint32_t buffer_offset
	}, ; 113: Microsoft.Extensions.FileProviders.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 7680, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26856032; uint32_t buffer_offset
	}, ; 114: Microsoft.Extensions.FileProviders.Composite
	%struct.CompressedAssemblyDescriptor {
		i32 19968, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26863712; uint32_t buffer_offset
	}, ; 115: Microsoft.Extensions.FileProviders.Physical
	%struct.CompressedAssemblyDescriptor {
		i32 27648, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26883680; uint32_t buffer_offset
	}, ; 116: Microsoft.Extensions.FileSystemGlobbing
	%struct.CompressedAssemblyDescriptor {
		i32 6144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26911328; uint32_t buffer_offset
	}, ; 117: Microsoft.Extensions.Hosting.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 44032, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26917472; uint32_t buffer_offset
	}, ; 118: Microsoft.Extensions.Http
	%struct.CompressedAssemblyDescriptor {
		i32 19968, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26961504; uint32_t buffer_offset
	}, ; 119: Microsoft.Extensions.Logging
	%struct.CompressedAssemblyDescriptor {
		i32 39424, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 26981472; uint32_t buffer_offset
	}, ; 120: Microsoft.Extensions.Logging.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 20992, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27020896; uint32_t buffer_offset
	}, ; 121: Microsoft.Extensions.Options
	%struct.CompressedAssemblyDescriptor {
		i32 13824, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27041888; uint32_t buffer_offset
	}, ; 122: Microsoft.Extensions.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 6144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27055712; uint32_t buffer_offset
	}, ; 123: Microsoft.IdentityModel.Abstractions
	%struct.CompressedAssemblyDescriptor {
		i32 27648, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27061856; uint32_t buffer_offset
	}, ; 124: Microsoft.IdentityModel.JsonWebTokens
	%struct.CompressedAssemblyDescriptor {
		i32 16384, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27089504; uint32_t buffer_offset
	}, ; 125: Microsoft.IdentityModel.Logging
	%struct.CompressedAssemblyDescriptor {
		i32 109056, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27105888; uint32_t buffer_offset
	}, ; 126: Microsoft.IdentityModel.Tokens
	%struct.CompressedAssemblyDescriptor {
		i32 43008, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27214944; uint32_t buffer_offset
	}, ; 127: Microsoft.JSInterop
	%struct.CompressedAssemblyDescriptor {
		i32 1928504, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 27257952; uint32_t buffer_offset
	}, ; 128: Microsoft.Maui.Controls
	%struct.CompressedAssemblyDescriptor {
		i32 135432, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 29186456; uint32_t buffer_offset
	}, ; 129: Microsoft.Maui.Controls.Xaml
	%struct.CompressedAssemblyDescriptor {
		i32 875832, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 29321888; uint32_t buffer_offset
	}, ; 130: Microsoft.Maui
	%struct.CompressedAssemblyDescriptor {
		i32 66048, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 30197720; uint32_t buffer_offset
	}, ; 131: Microsoft.Maui.Essentials
	%struct.CompressedAssemblyDescriptor {
		i32 208696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 30263768; uint32_t buffer_offset
	}, ; 132: Microsoft.Maui.Graphics
	%struct.CompressedAssemblyDescriptor {
		i32 723368, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 30472464; uint32_t buffer_offset
	}, ; 133: Newtonsoft.Json
	%struct.CompressedAssemblyDescriptor {
		i32 287648, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31195832; uint32_t buffer_offset
	}, ; 134: Polly
	%struct.CompressedAssemblyDescriptor {
		i32 19968, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31483480; uint32_t buffer_offset
	}, ; 135: Polly.Core
	%struct.CompressedAssemblyDescriptor {
		i32 218624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31503448; uint32_t buffer_offset
	}, ; 136: Skender.Stock.Indicators
	%struct.CompressedAssemblyDescriptor {
		i32 78848, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31722072; uint32_t buffer_offset
	}, ; 137: SkiaSharp
	%struct.CompressedAssemblyDescriptor {
		i32 50248, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31800920; uint32_t buffer_offset
	}, ; 138: SkiaSharp.Views.Android
	%struct.CompressedAssemblyDescriptor {
		i32 26144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31851168; uint32_t buffer_offset
	}, ; 139: SkiaSharp.Views.Maui.Controls
	%struct.CompressedAssemblyDescriptor {
		i32 33824, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31877312; uint32_t buffer_offset
	}, ; 140: SkiaSharp.Views.Maui.Core
	%struct.CompressedAssemblyDescriptor {
		i32 5632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31911136; uint32_t buffer_offset
	}, ; 141: SQLitePCLRaw.batteries_v2
	%struct.CompressedAssemblyDescriptor {
		i32 51200, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31916768; uint32_t buffer_offset
	}, ; 142: SQLitePCLRaw.core
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31967968; uint32_t buffer_offset
	}, ; 143: SQLitePCLRaw.lib.e_sqlite3.android
	%struct.CompressedAssemblyDescriptor {
		i32 36864, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 31973088; uint32_t buffer_offset
	}, ; 144: SQLitePCLRaw.provider.e_sqlite3
	%struct.CompressedAssemblyDescriptor {
		i32 23552, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32009952; uint32_t buffer_offset
	}, ; 145: System.IdentityModel.Tokens.Jwt
	%struct.CompressedAssemblyDescriptor {
		i32 167424, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32033504; uint32_t buffer_offset
	}, ; 146: System.Reactive
	%struct.CompressedAssemblyDescriptor {
		i32 73216, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32200928; uint32_t buffer_offset
	}, ; 147: Xamarin.AndroidX.Activity
	%struct.CompressedAssemblyDescriptor {
		i32 582656, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32274144; uint32_t buffer_offset
	}, ; 148: Xamarin.AndroidX.AppCompat
	%struct.CompressedAssemblyDescriptor {
		i32 17408, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32856800; uint32_t buffer_offset
	}, ; 149: Xamarin.AndroidX.AppCompat.AppCompatResources
	%struct.CompressedAssemblyDescriptor {
		i32 18944, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32874208; uint32_t buffer_offset
	}, ; 150: Xamarin.AndroidX.CardView
	%struct.CompressedAssemblyDescriptor {
		i32 22528, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32893152; uint32_t buffer_offset
	}, ; 151: Xamarin.AndroidX.Collection.Jvm
	%struct.CompressedAssemblyDescriptor {
		i32 78336, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32915680; uint32_t buffer_offset
	}, ; 152: Xamarin.AndroidX.CoordinatorLayout
	%struct.CompressedAssemblyDescriptor {
		i32 595456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 32994016; uint32_t buffer_offset
	}, ; 153: Xamarin.AndroidX.Core
	%struct.CompressedAssemblyDescriptor {
		i32 26624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33589472; uint32_t buffer_offset
	}, ; 154: Xamarin.AndroidX.CursorAdapter
	%struct.CompressedAssemblyDescriptor {
		i32 9728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33616096; uint32_t buffer_offset
	}, ; 155: Xamarin.AndroidX.CustomView
	%struct.CompressedAssemblyDescriptor {
		i32 46592, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33625824; uint32_t buffer_offset
	}, ; 156: Xamarin.AndroidX.DrawerLayout
	%struct.CompressedAssemblyDescriptor {
		i32 233984, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33672416; uint32_t buffer_offset
	}, ; 157: Xamarin.AndroidX.Fragment
	%struct.CompressedAssemblyDescriptor {
		i32 23552, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33906400; uint32_t buffer_offset
	}, ; 158: Xamarin.AndroidX.Lifecycle.Common.Jvm
	%struct.CompressedAssemblyDescriptor {
		i32 18944, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33929952; uint32_t buffer_offset
	}, ; 159: Xamarin.AndroidX.Lifecycle.LiveData.Core
	%struct.CompressedAssemblyDescriptor {
		i32 32768, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33948896; uint32_t buffer_offset
	}, ; 160: Xamarin.AndroidX.Lifecycle.ViewModel.Android
	%struct.CompressedAssemblyDescriptor {
		i32 13824, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33981664; uint32_t buffer_offset
	}, ; 161: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android
	%struct.CompressedAssemblyDescriptor {
		i32 39424, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 33995488; uint32_t buffer_offset
	}, ; 162: Xamarin.AndroidX.Loader
	%struct.CompressedAssemblyDescriptor {
		i32 92672, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34034912; uint32_t buffer_offset
	}, ; 163: Xamarin.AndroidX.Navigation.Common.Android
	%struct.CompressedAssemblyDescriptor {
		i32 19456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34127584; uint32_t buffer_offset
	}, ; 164: Xamarin.AndroidX.Navigation.Fragment
	%struct.CompressedAssemblyDescriptor {
		i32 65024, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34147040; uint32_t buffer_offset
	}, ; 165: Xamarin.AndroidX.Navigation.Runtime.Android
	%struct.CompressedAssemblyDescriptor {
		i32 27136, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34212064; uint32_t buffer_offset
	}, ; 166: Xamarin.AndroidX.Navigation.UI
	%struct.CompressedAssemblyDescriptor {
		i32 454144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34239200; uint32_t buffer_offset
	}, ; 167: Xamarin.AndroidX.RecyclerView
	%struct.CompressedAssemblyDescriptor {
		i32 12288, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34693344; uint32_t buffer_offset
	}, ; 168: Xamarin.AndroidX.SavedState.SavedState.Android
	%struct.CompressedAssemblyDescriptor {
		i32 24576, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34705632; uint32_t buffer_offset
	}, ; 169: Xamarin.AndroidX.Security.SecurityCrypto
	%struct.CompressedAssemblyDescriptor {
		i32 41472, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34730208; uint32_t buffer_offset
	}, ; 170: Xamarin.AndroidX.SwipeRefreshLayout
	%struct.CompressedAssemblyDescriptor {
		i32 62464, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34771680; uint32_t buffer_offset
	}, ; 171: Xamarin.AndroidX.ViewPager
	%struct.CompressedAssemblyDescriptor {
		i32 39936, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34834144; uint32_t buffer_offset
	}, ; 172: Xamarin.AndroidX.ViewPager2
	%struct.CompressedAssemblyDescriptor {
		i32 674304, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 34874080; uint32_t buffer_offset
	}, ; 173: Xamarin.Google.Android.Material
	%struct.CompressedAssemblyDescriptor {
		i32 345088, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 35548384; uint32_t buffer_offset
	}, ; 174: Xamarin.Google.Crypto.Tink.Android
	%struct.CompressedAssemblyDescriptor {
		i32 90624, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 35893472; uint32_t buffer_offset
	}, ; 175: Xamarin.Kotlin.StdLib
	%struct.CompressedAssemblyDescriptor {
		i32 28672, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 35984096; uint32_t buffer_offset
	}, ; 176: Xamarin.KotlinX.Coroutines.Core.Jvm
	%struct.CompressedAssemblyDescriptor {
		i32 91648, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 36012768; uint32_t buffer_offset
	}, ; 177: Xamarin.KotlinX.Serialization.Core.Jvm
	%struct.CompressedAssemblyDescriptor {
		i32 801792, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 36104416; uint32_t buffer_offset
	}, ; 178: AccessibleTrader.Core
	%struct.CompressedAssemblyDescriptor {
		i32 35840, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 36906208; uint32_t buffer_offset
	}, ; 179: AccessibleTrader.Plugins.Alpaca
	%struct.CompressedAssemblyDescriptor {
		i32 39424, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 36942048; uint32_t buffer_offset
	}, ; 180: AccessibleTrader.Plugins.Binance
	%struct.CompressedAssemblyDescriptor {
		i32 38400, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 36981472; uint32_t buffer_offset
	}, ; 181: AccessibleTrader.Plugins.Bitstamp
	%struct.CompressedAssemblyDescriptor {
		i32 34304, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37019872; uint32_t buffer_offset
	}, ; 182: AccessibleTrader.Plugins.Coinbase
	%struct.CompressedAssemblyDescriptor {
		i32 12288, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37054176; uint32_t buffer_offset
	}, ; 183: AccessibleTrader.Plugins.Fred
	%struct.CompressedAssemblyDescriptor {
		i32 20480, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37066464; uint32_t buffer_offset
	}, ; 184: AccessibleTrader.Plugins.Polygon
	%struct.CompressedAssemblyDescriptor {
		i32 266240, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37086944; uint32_t buffer_offset
	}, ; 185: AccessibleTrader.Sdk
	%struct.CompressedAssemblyDescriptor {
		i32 360960, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37353184; uint32_t buffer_offset
	}, ; 186: AccessibleTrader.BlazorClient
	%struct.CompressedAssemblyDescriptor {
		i32 229888, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37714144; uint32_t buffer_offset
	}, ; 187: Microsoft.CSharp
	%struct.CompressedAssemblyDescriptor {
		i32 35840, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37944032; uint32_t buffer_offset
	}, ; 188: System.Collections.Concurrent
	%struct.CompressedAssemblyDescriptor {
		i32 166912, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 37979872; uint32_t buffer_offset
	}, ; 189: System.Collections.Immutable
	%struct.CompressedAssemblyDescriptor {
		i32 19456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38146784; uint32_t buffer_offset
	}, ; 190: System.Collections.NonGeneric
	%struct.CompressedAssemblyDescriptor {
		i32 16896, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38166240; uint32_t buffer_offset
	}, ; 191: System.Collections.Specialized
	%struct.CompressedAssemblyDescriptor {
		i32 73728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38183136; uint32_t buffer_offset
	}, ; 192: System.Collections
	%struct.CompressedAssemblyDescriptor {
		i32 6656, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38256864; uint32_t buffer_offset
	}, ; 193: System.ComponentModel.Annotations
	%struct.CompressedAssemblyDescriptor {
		i32 15360, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38263520; uint32_t buffer_offset
	}, ; 194: System.ComponentModel.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 153088, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38278880; uint32_t buffer_offset
	}, ; 195: System.ComponentModel.TypeConverter
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38431968; uint32_t buffer_offset
	}, ; 196: System.ComponentModel
	%struct.CompressedAssemblyDescriptor {
		i32 13312, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38437088; uint32_t buffer_offset
	}, ; 197: System.Console
	%struct.CompressedAssemblyDescriptor {
		i32 567296, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 38450400; uint32_t buffer_offset
	}, ; 198: System.Data.Common
	%struct.CompressedAssemblyDescriptor {
		i32 71168, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39017696; uint32_t buffer_offset
	}, ; 199: System.Diagnostics.DiagnosticSource
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39088864; uint32_t buffer_offset
	}, ; 200: System.Diagnostics.StackTrace
	%struct.CompressedAssemblyDescriptor {
		i32 20480, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39093984; uint32_t buffer_offset
	}, ; 201: System.Diagnostics.TraceSource
	%struct.CompressedAssemblyDescriptor {
		i32 5632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39114464; uint32_t buffer_offset
	}, ; 202: System.Diagnostics.Tracing
	%struct.CompressedAssemblyDescriptor {
		i32 36864, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39120096; uint32_t buffer_offset
	}, ; 203: System.Drawing.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39156960; uint32_t buffer_offset
	}, ; 204: System.Drawing
	%struct.CompressedAssemblyDescriptor {
		i32 61952, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39162080; uint32_t buffer_offset
	}, ; 205: System.Formats.Asn1
	%struct.CompressedAssemblyDescriptor {
		i32 4608, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39224032; uint32_t buffer_offset
	}, ; 206: System.Globalization
	%struct.CompressedAssemblyDescriptor {
		i32 22016, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39228640; uint32_t buffer_offset
	}, ; 207: System.IO.Compression.Brotli
	%struct.CompressedAssemblyDescriptor {
		i32 114176, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39250656; uint32_t buffer_offset
	}, ; 208: System.IO.Compression
	%struct.CompressedAssemblyDescriptor {
		i32 30720, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39364832; uint32_t buffer_offset
	}, ; 209: System.IO.FileSystem.Watcher
	%struct.CompressedAssemblyDescriptor {
		i32 26112, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39395552; uint32_t buffer_offset
	}, ; 210: System.IO.MemoryMappedFiles
	%struct.CompressedAssemblyDescriptor {
		i32 6144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39421664; uint32_t buffer_offset
	}, ; 211: System.IO.Pipelines
	%struct.CompressedAssemblyDescriptor {
		i32 477696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39427808; uint32_t buffer_offset
	}, ; 212: System.Linq.Expressions
	%struct.CompressedAssemblyDescriptor {
		i32 59392, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39905504; uint32_t buffer_offset
	}, ; 213: System.Linq.Parallel
	%struct.CompressedAssemblyDescriptor {
		i32 55808, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 39964896; uint32_t buffer_offset
	}, ; 214: System.Linq.Queryable
	%struct.CompressedAssemblyDescriptor {
		i32 161792, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40020704; uint32_t buffer_offset
	}, ; 215: System.Linq
	%struct.CompressedAssemblyDescriptor {
		i32 16896, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40182496; uint32_t buffer_offset
	}, ; 216: System.Memory
	%struct.CompressedAssemblyDescriptor {
		i32 378368, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40199392; uint32_t buffer_offset
	}, ; 217: System.Net.Http
	%struct.CompressedAssemblyDescriptor {
		i32 29184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40577760; uint32_t buffer_offset
	}, ; 218: System.Net.NameResolution
	%struct.CompressedAssemblyDescriptor {
		i32 29184, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40606944; uint32_t buffer_offset
	}, ; 219: System.Net.NetworkInformation
	%struct.CompressedAssemblyDescriptor {
		i32 69120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40636128; uint32_t buffer_offset
	}, ; 220: System.Net.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 7680, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40705248; uint32_t buffer_offset
	}, ; 221: System.Net.Requests
	%struct.CompressedAssemblyDescriptor {
		i32 150528, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40712928; uint32_t buffer_offset
	}, ; 222: System.Net.Security
	%struct.CompressedAssemblyDescriptor {
		i32 103936, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40863456; uint32_t buffer_offset
	}, ; 223: System.Net.Sockets
	%struct.CompressedAssemblyDescriptor {
		i32 14848, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40967392; uint32_t buffer_offset
	}, ; 224: System.Net.WebHeaderCollection
	%struct.CompressedAssemblyDescriptor {
		i32 10752, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40982240; uint32_t buffer_offset
	}, ; 225: System.Net.WebProxy
	%struct.CompressedAssemblyDescriptor {
		i32 29696, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 40992992; uint32_t buffer_offset
	}, ; 226: System.Net.WebSockets.Client
	%struct.CompressedAssemblyDescriptor {
		i32 59392, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41022688; uint32_t buffer_offset
	}, ; 227: System.Net.WebSockets
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41082080; uint32_t buffer_offset
	}, ; 228: System.Numerics.Vectors
	%struct.CompressedAssemblyDescriptor {
		i32 20992, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41087200; uint32_t buffer_offset
	}, ; 229: System.ObjectModel
	%struct.CompressedAssemblyDescriptor {
		i32 76288, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41108192; uint32_t buffer_offset
	}, ; 230: System.Private.Uri
	%struct.CompressedAssemblyDescriptor {
		i32 55296, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41184480; uint32_t buffer_offset
	}, ; 231: System.Private.Xml.Linq
	%struct.CompressedAssemblyDescriptor {
		i32 1453568, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 41239776; uint32_t buffer_offset
	}, ; 232: System.Private.Xml
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42693344; uint32_t buffer_offset
	}, ; 233: System.Reflection.Emit.ILGeneration
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42698464; uint32_t buffer_offset
	}, ; 234: System.Reflection.Emit.Lightweight
	%struct.CompressedAssemblyDescriptor {
		i32 277504, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42703584; uint32_t buffer_offset
	}, ; 235: System.Reflection.Metadata
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42981088; uint32_t buffer_offset
	}, ; 236: System.Reflection.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42986208; uint32_t buffer_offset
	}, ; 237: System.Runtime.InteropServices.RuntimeInformation
	%struct.CompressedAssemblyDescriptor {
		i32 9728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 42991328; uint32_t buffer_offset
	}, ; 238: System.Runtime.InteropServices
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43001056; uint32_t buffer_offset
	}, ; 239: System.Runtime.Intrinsics
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43006176; uint32_t buffer_offset
	}, ; 240: System.Runtime.Loader
	%struct.CompressedAssemblyDescriptor {
		i32 98816, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43011296; uint32_t buffer_offset
	}, ; 241: System.Runtime.Numerics
	%struct.CompressedAssemblyDescriptor {
		i32 8192, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43110112; uint32_t buffer_offset
	}, ; 242: System.Runtime.Serialization.Formatters
	%struct.CompressedAssemblyDescriptor {
		i32 6656, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43118304; uint32_t buffer_offset
	}, ; 243: System.Runtime.Serialization.Primitives
	%struct.CompressedAssemblyDescriptor {
		i32 19456, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43124960; uint32_t buffer_offset
	}, ; 244: System.Runtime
	%struct.CompressedAssemblyDescriptor {
		i32 6144, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43144416; uint32_t buffer_offset
	}, ; 245: System.Security.Claims
	%struct.CompressedAssemblyDescriptor {
		i32 239104, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43150560; uint32_t buffer_offset
	}, ; 246: System.Security.Cryptography
	%struct.CompressedAssemblyDescriptor {
		i32 699904, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 43389664; uint32_t buffer_offset
	}, ; 247: System.Text.Encoding.CodePages
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44089568; uint32_t buffer_offset
	}, ; 248: System.Text.Encoding.Extensions
	%struct.CompressedAssemblyDescriptor {
		i32 31232, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44094688; uint32_t buffer_offset
	}, ; 249: System.Text.Encodings.Web
	%struct.CompressedAssemblyDescriptor {
		i32 400384, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44125920; uint32_t buffer_offset
	}, ; 250: System.Text.Json
	%struct.CompressedAssemblyDescriptor {
		i32 337408, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44526304; uint32_t buffer_offset
	}, ; 251: System.Text.RegularExpressions
	%struct.CompressedAssemblyDescriptor {
		i32 27648, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44863712; uint32_t buffer_offset
	}, ; 252: System.Threading.Channels
	%struct.CompressedAssemblyDescriptor {
		i32 15872, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44891360; uint32_t buffer_offset
	}, ; 253: System.Threading.Tasks.Parallel
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44907232; uint32_t buffer_offset
	}, ; 254: System.Threading.Thread
	%struct.CompressedAssemblyDescriptor {
		i32 9728, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44912352; uint32_t buffer_offset
	}, ; 255: System.Threading
	%struct.CompressedAssemblyDescriptor {
		i32 41984, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44922080; uint32_t buffer_offset
	}, ; 256: System.Transactions.Local
	%struct.CompressedAssemblyDescriptor {
		i32 10752, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44964064; uint32_t buffer_offset
	}, ; 257: System.Web.HttpUtility
	%struct.CompressedAssemblyDescriptor {
		i32 4608, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44974816; uint32_t buffer_offset
	}, ; 258: System.Xml.Linq
	%struct.CompressedAssemblyDescriptor {
		i32 5632, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44979424; uint32_t buffer_offset
	}, ; 259: System.Xml.ReaderWriter
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44985056; uint32_t buffer_offset
	}, ; 260: System.Xml.XDocument
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44990176; uint32_t buffer_offset
	}, ; 261: System.Xml.XPath.XDocument
	%struct.CompressedAssemblyDescriptor {
		i32 5120, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 44995296; uint32_t buffer_offset
	}, ; 262: System
	%struct.CompressedAssemblyDescriptor {
		i32 12288, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 45000416; uint32_t buffer_offset
	}, ; 263: netstandard
	%struct.CompressedAssemblyDescriptor {
		i32 2529280, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 45012704; uint32_t buffer_offset
	}, ; 264: System.Private.CoreLib
	%struct.CompressedAssemblyDescriptor {
		i32 171008, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 47541984; uint32_t buffer_offset
	}, ; 265: Java.Interop
	%struct.CompressedAssemblyDescriptor {
		i32 22560, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 47712992; uint32_t buffer_offset
	}, ; 266: Mono.Android.Runtime
	%struct.CompressedAssemblyDescriptor {
		i32 2267136, ; uint32_t uncompressed_file_size
		i1 false, ; bool loaded
		i32 47735552; uint32_t buffer_offset
	} ; 267: Mono.Android
], align 16

@uncompressed_assemblies_data_size = dso_local local_unnamed_addr constant i32 50002688, align 4

@uncompressed_assemblies_data_buffer = dso_local local_unnamed_addr global [50002688 x i8] zeroinitializer, align 16

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
