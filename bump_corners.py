import re
import math

def process_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    def replacer(match):
        val = match.group(1)
        if ',' in val:
            parts = [str(math.ceil(int(p) * 1.5)) for p in val.split(',')]
            new_val = ','.join(parts)
        else:
            new_val = str(math.ceil(int(val) * 1.5))
        return f'CornerRadius="{new_val}"'

    new_content = re.sub(r'CornerRadius="([0-9,]+)"', replacer, content)
    
    with open(filepath, 'w') as f:
        f.write(new_content)

process_file('src/AstraClient/Themes/Controls.xaml')
process_file('src/AstraClient/Themes/Glass.xaml')
