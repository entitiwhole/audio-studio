import hashlib

files = [
    r'C:\Users\Admin\Desktop\app.ico',
    r'E:\Atest1.0\AudioStudio\AudioStudio\app.ico'
]

for f in files:
    with open(f, 'rb') as file:
        data = file.read()
        md5 = hashlib.md5(data).hexdigest()
        print(f"{f}: {len(data)} bytes, MD5: {md5}")
