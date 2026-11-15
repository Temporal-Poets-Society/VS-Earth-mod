import sys
d, src, defs = sys.argv[1], sys.argv[2], sys.argv[3]
open(d + '/dg.csproj', 'w').write('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>12</LangVersion><Nullable>disable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <DefineConstants>$(DefineConstants);%s</DefineConstants><NoWarn>CS0649;CS0169;CS0414;CS8632;CS0219;CS0168</NoWarn><ServerGarbageCollection>true</ServerGarbageCollection></PropertyGroup>
  <ItemGroup>
    <Compile Include="..\digest-src\*.cs" />
    <Compile Include="%s\src\Core\*.cs" Link="Core\%%(Filename)%%(Extension)" />
    <Compile Include="%s\src\EarthParams.cs;%s\src\EarthMapConfig.cs" />
  </ItemGroup>
</Project>
''' % (defs, src, src, src))
