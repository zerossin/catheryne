"""HoYoLAB metadata is a projection of the full validated game catalog bundle."""
import pathlib
import runpy
if __name__ == '__main__':
    runpy.run_path(str(pathlib.Path(__file__).with_name('update-game-catalog.py')), run_name='__main__')
