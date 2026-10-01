# -*- coding: utf-8 -*-
"""写入构建版本戳：城镇页左下角显示当前短哈希，用户一眼能确认跑的是不是最新代码。
用法：python Tools/write-build-stamp.py（在提交后跑，把结果并入下一次提交）
"""
import subprocess, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'GameClient/TwelveJade/Assets/Resources/Config/build-stamp.txt')

def run(*args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True).stdout.strip()

# 版本戳就是"运行中的代码来自哪个提交"：写当前 HEAD 的短哈希即可。
# 它天然比含它的那个提交早一格（自指无法消除），但足以判断"是不是最新那一批改动"。
# 提交后跑一次并把结果并入下一次提交。
head = run('git', 'rev-parse', '--short', 'HEAD')
stamp = head
os.makedirs(os.path.dirname(OUT), exist_ok=True)
prev = ''
if os.path.exists(OUT):
    prev = open(OUT, encoding='utf-8').read().strip()
if prev == stamp:
    print('unchanged:', stamp)
    sys.exit(0)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    f.write(stamp + '\n')
print('build stamp:', prev or '(空)', '->', stamp)
