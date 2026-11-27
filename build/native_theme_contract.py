"""Validate actual XAML brush consumers against both semantic palette dictionaries."""
import re
from xml.etree import ElementTree as ET

KEY="{http://schemas.microsoft.com/winfx/2006/xaml}Key"
BRUSH="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}SolidColorBrush"

def read_brushes(text):
    result={}
    for node in ET.fromstring(text):
        if node.tag != BRUSH:
            continue
        key=node.attrib[KEY]
        value=node.attrib["Color"].upper()
        if key in result or not re.fullmatch(r"#[0-9A-F]{6}",value):
            raise ValueError("Invalid or duplicate semantic brush: "+key)
        result[key]=value
    return result

def check_visual_resources(view,light,dark,app,required):
    errors=[]
    try:
        a,b=read_brushes(light),read_brushes(dark)
        root=ET.fromstring(view)
        app_root=ET.fromstring(app)
        sources=[node.attrib.get("Source") for node in app_root.iter()]
        if "Themes/Light.xaml" not in sources or "Themes/Controls.xaml" not in sources:
            errors.append("Native default palette and shared controls must be loaded")
        used=set()
        for node in root.iter():
            for prop,value in node.attrib.items():
                if prop=="Value" and node.tag.endswith("}Setter"):
                    prop=node.attrib.get("Property","")
                if prop not in ("Background","Foreground","BorderBrush","RowBackground",
                                "AlternatingRowBackground","HorizontalGridLinesBrush","VerticalGridLinesBrush"):
                    continue
                match=re.fullmatch(r"\{DynamicResource (Native\w+)\}",value)
                if match:
                    used.add((prop,match.group(1)))
        for prop,key,expected in required:
            if (prop,key) not in used:
                errors.append(f"Missing actual {prop} consumer of {key}")
            if a.get(key)!=expected or key not in b:
                errors.append(f"Invalid light/dark definition of {key}, expected light {expected}")
        if a.keys()!=b.keys():
            errors.append("Native palettes must share all brush keys")
        if a.get("NativeSurface")==b.get("NativeSurface"):
            errors.append("Dark palette must differ from light palette")
    except (ET.ParseError,KeyError,ValueError) as exc:
        errors.append("Invalid native theme resources: "+str(exc))
    return errors
