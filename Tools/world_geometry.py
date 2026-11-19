"""Shared road width used by generation, mask, elevation and overlap checks."""
import numpy as np

def main_road_half(y, terrain):
    road=terrain['mainRoad']; values=np.asarray(y,dtype=float)
    half=np.full_like(values,float(road['half']))
    for zone in road.get('widthZones',[]):
        distance=np.maximum(np.maximum(zone['y0']-values,values-zone['y1']),0)
        blend=float(zone.get('blend',0))
        weight=np.clip(1-distance/max(blend,1e-9),0,1)
        half=half*(1-weight)+float(zone['half'])*weight
    return half
