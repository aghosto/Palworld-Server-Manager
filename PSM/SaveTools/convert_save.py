import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from palworld_save_tools.commands.convert import main

if __name__ == "__main__":
    sys.exit(main())
