#!/usr/bin/env python3
"""Static native palette contract; physical Windows acceptance is separate."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re

ROOT=Path(__file__).resolve().parents[1]
NATIVE=ROOT/"src/AlTamam.NativeDesktop"
XKEY="{http://schemas.microsoft.com/winfx/2006/xaml}Key"

def palette(name):
    root=ET.parse(NATIVE/"Themes"/(name+".xaml")).getroot()
    values={}
    for item in root:
        key=item.attrib.get(XKEY)
        value=item.attrib.get("Color")
        if key and value:
            assert key not in values, f"Duplicate brush: {key}"
            values[key]=value
    return values

def luminance(value):
    assert re.fullmatch(r"#[0-9A-Fa-f]{6}",value), value
    rgb=[int(value[i:i+2],16)/255 for i in (1,3,5)]
    linear=[v/12.92 if v<=0.04045 else ((v+0.055)/1.055)**2.4 for v in rgb]
    return sum(a*b for a,b in zip(linear,(0.2126,0.7152,0.0722)))

def contrast(a,b):
    hi,lo=sorted((luminance(a),luminance(b)),reverse=True)
    return (hi+0.05)/(lo+0.05)

def validate():
    light,dark=palette("Light"),palette("Dark")
    assert light.keys()==dark.keys(), "Theme dictionaries must have identical keys"
    assert light["NativeSurface"]!=dark["NativeSurface"], "Dark palette is not a light alias"
    assert light["NativeCanvas"]!=dark["NativeCanvas"]
    for name,colors in (("light",light),("dark",dark)):
        for fg in ("NativeText","NativeHeading","NativeMuted","NativeSecondary","NativeAccentText",
                   "NativeSuccessText","NativeDangerText","NativeWarningText"):
            for bg in ("NativeCanvas","NativeSurface","NativeSubtle"):
                assert contrast(colors[fg],colors[bg])>=4.5, f"{name}: insufficient contrast {fg}/{bg}"
        for fg,bg in (("NativeOnStrong","NativeHeader"),("NativeOnStrong","NativeHeaderCard"),
                      ("NativeOnStrong","NativeAccentFill"),("NativeOnStrong","NativeDangerFill"),
                      ("NativeHeaderMuted","NativeHeader"),("NativeHeaderAccent","NativeHeaderCard"),
                      ("NativeWarningText","NativeWarningSurface"),("NativeWarningText","NativeWarmSurface"),
                      ("NativeText","NativeInfoSurface"),("NativeText","NativePaleWarning"),
                      ("NativeText","NativeNavSurface")):
            assert contrast(colors[fg],colors[bg])>=4.5, f"{name}: insufficient contrast {fg}/{bg}"
    for path in [NATIVE/"MainWindow.xaml",*sorted((NATIVE/"Views").glob("*.xaml"))]:
        text=path.read_text(encoding="utf-8-sig")
        ET.fromstring(text)
        for key in re.findall(r"\{DynamicResource (Native\w+)\}",text):
            assert key in light, f"Unknown palette brush {path.name}: {key}"
        assert not re.search(r'(?:Background|Foreground|BorderBrush|RowBackground|AlternatingRowBackground)="(?:White|#FFFFFF|#F1F5F9|#F8FAFC|#FFFBEB|#0F2942|#475569|#CBD5E1)"',text,re.I), f"Unconverted surface or text brush: {path.name}"
    app=(NATIVE/"App.xaml").read_text(encoding="utf-8-sig")
    assert 'Source="Themes/Light.xaml"' in app
    assert 'Source="Themes/Controls.xaml"' in app
    host=(NATIVE/"MainWindow.xaml.cs").read_text(encoding="utf-8-sig")
    assert "ReadSavedThemeAsync" in host and "ApplySavedTheme" in host
    assert "Overview.Snapshot" in host
    print("NATIVE THEME PASS: matching palette keys, contrast, all native XAML resource references and startup/save binding")

if __name__=="__main__":
    validate()
