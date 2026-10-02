# -*- coding: utf-8 -*-
"""十二玉楼生图工具：调本地 grok2api 网关（D:\\work\\amaze\\DDyu，127.0.0.1:8000）生成美术图。

用法：
  python Tools/generate-art.py <prompt文件> <输出文件> [模型]
  模型默认 grok-imagine-image（grok-imagine-image-quality 对 Basic tier key 不可用）。

key 获取（明文只显示一次，存在服务端库里的密文不可逆）：
  1. POST /api/admin/v1/auth/login，body = DDyu/config.yaml 的 bootstrapAdmin，
     取 data.tokens.accessToken（TTL 短，过期就重新登录）；
  2. POST /api/admin/v1/client-keys {"name": "...", "enabled": true}；
  3. GET /api/admin/v1/client-keys/{id}/secret → data.secret 即 Bearer key。
  已有 key 存 /tmp/ddyu-art-key.txt 时本脚本直接使用。key 明文不要入库、不要写进记忆。
"""
import json, sys, os, urllib.request

BASE = 'http://127.0.0.1:8000'
KEY_FILE = '/tmp/ddyu-art-key.txt'
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def opener():
    # 本机地址不走系统代理（环境里 https_proxy 会拦截 127.0.0.1）。
    return urllib.request.build_opener(urllib.request.ProxyHandler({}))

def ensure_key():
    if os.path.exists(KEY_FILE) and os.path.getsize(KEY_FILE) > 10:
        return open(KEY_FILE).read().strip()
    sys.exit('缺少 key：' + KEY_FILE + ' 不存在。按文件头注释重新创建（需管理员登录）。')

def generate(prompt, model='grok-imagine-image'):
    op = opener()
    body = json.dumps({'model': model, 'prompt': prompt, 'n': 1}).encode()
    req = urllib.request.Request(BASE + '/v1/images/generations', data=body,
        headers={'Authorization': 'Bearer ' + ensure_key(), 'Content-Type': 'application/json'})
    r = json.loads(op.open(req, timeout=300).read())
    return op.open(r['data'][0]['url'], timeout=60).read()

if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    prompt = open(sys.argv[1], encoding='utf-8').read()
    out = sys.argv[2]
    model = sys.argv[3] if len(sys.argv) > 3 else 'grok-imagine-image'
    data = generate(prompt, model)
    os.makedirs(os.path.dirname(out) or '.', exist_ok=True)
    open(out, 'wb').write(data)
    from PIL import Image
    im = Image.open(out)
    print(out, im.size, len(data) // 1024, 'KB')
