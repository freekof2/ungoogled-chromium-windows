#!/usr/bin/env python3
# -*- coding: utf-8 -*-

# Copyright (c) 2019 The ungoogled-chromium Authors. All rights reserved.
# Use of this source code is governed by a BSD-style license that can be
# found in the LICENSE file.
"""
ungoogled-chromium build script for Microsoft Windows
"""

import sys
import time
import argparse
import os
import re
import shutil
import subprocess
import ctypes
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / 'ungoogled-chromium' / 'utils'))
import downloads
import domain_substitution
import prune_binaries
import patches
from _common import ENCODING, USE_REGISTRY, ExtractorEnum, get_logger
sys.path.pop(0)

_ROOT_DIR = Path(__file__).resolve().parent
_PATCH_BIN_RELPATH = Path('third_party/git/usr/bin/patch.exe')
_CI_STAGE_TIMEOUT_EXIT_CODE = 2

# Ninja must share the 6-hour GitHub job budget with the checkpoint download/unzip
# that happened before it and the 7z + upload that come after.  Observed on
# build-8: 9 min download + 1h41m unzip consumed the stage before ninja even ran,
# yet ninja still claimed a hardcoded 3.5h, so the job was killed mid-upload.
# The JS stage action now computes wall-clock elapsed before ninja and passes it
# via --ci-ninja-timeout so that download/unzip time is accounted for.
_PROCESS_START_MONOTONIC = time.monotonic()
_CI_JOB_BUDGET_SECONDS = 6 * 60 * 60          # GitHub hosted runner limit
_CI_POST_NINJA_RESERVE_SECONDS = 110 * 60     # 7z + upload for 13 GB tree
_CI_MIN_NINJA_SECONDS = 60 * 60               # always allow ninja at least 1h
_CI_NINJA_TIMEOUT_ENV = 'STAGE_NINJA_TIMEOUT' # alternative env var set by JS


def _ninja_timeout_seconds(external_timeout=None):
    """Return the dynamic ninja budget for the current CI stage.

    If external_timeout (int seconds) is provided by the JS stage action,
    it already accounts for job elapsed before ninja, so use it directly
    (clamped to minimum). Otherwise fall back to monotonic elapsed inside
    this process.
    """
    if external_timeout is not None:
        try:
            t = int(external_timeout)
            return max(_CI_MIN_NINJA_SECONDS, t)
        except (ValueError, TypeError):
            pass
    env_val = os.environ.get(_CI_NINJA_TIMEOUT_ENV)
    if env_val:
        try:
            t = int(env_val)
            return max(_CI_MIN_NINJA_SECONDS, t)
        except (ValueError, TypeError):
            pass
    elapsed = time.monotonic() - _PROCESS_START_MONOTONIC
    budget = _CI_JOB_BUDGET_SECONDS - _CI_POST_NINJA_RESERVE_SECONDS - elapsed
    return max(_CI_MIN_NINJA_SECONDS, budget)

def _get_vcvars_path(name='64'):
    """
    Returns the path to the corresponding vcvars*.bat path

    As of VS 2017, name can be one of: 32, 64, all, amd64_x86, x86_amd64
    """
    vswhere_exe = '%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe'
    result = subprocess.run(
        '"{}" -products * -prerelease -latest -property installationPath'.format(vswhere_exe),
        shell=True,
        check=True,
        stdout=subprocess.PIPE,
        universal_newlines=True)
    vcvars_path = Path(result.stdout.strip(), 'VC/Auxiliary/Build/vcvars{}.bat'.format(name))
    if not vcvars_path.exists():
        raise RuntimeError(
            'Could not find vcvars batch script in expected location: {}'.format(vcvars_path))
    return vcvars_path


def _run_build_process(*args, **kwargs):
    """
    Runs the subprocess with the correct environment variables for building
    """
    # Add call to set VC variables
    cmd_input = ['call "%s" >nul' % _get_vcvars_path()]
    cmd_input.append('set DEPOT_TOOLS_WIN_TOOLCHAIN=0')
    cmd_input.append('set NODE_OPTIONS=--max-old-space-size=8192')
    cmd_input.append(' '.join(map('"{}"'.format, args)))
    cmd_input.append('exit\n')
    subprocess.run(('cmd.exe', '/k'),
                   input='\n'.join(cmd_input),
                   check=True,
                   encoding=ENCODING,
                   **kwargs)


def _run_build_process_timeout(*args, timeout):
    """
    Runs the subprocess with the correct environment variables for building
    """
    # Add call to set VC variables
    cmd_input = ['call "%s" >nul' % _get_vcvars_path()]
    cmd_input.append('set DEPOT_TOOLS_WIN_TOOLCHAIN=0')
    cmd_input.append('set NODE_OPTIONS=--max-old-space-size=8192')
    cmd_input.append(' '.join(map('"{}"'.format, args)))
    cmd_input.append('exit\n')
    with subprocess.Popen(('cmd.exe', '/k'), encoding=ENCODING, stdin=subprocess.PIPE, creationflags=subprocess.CREATE_NEW_PROCESS_GROUP) as proc:
        proc.stdin.write('\n'.join(cmd_input))
        proc.stdin.close()
        try:
            proc.wait(timeout)
            if proc.returncode != 0:
                raise RuntimeError('Build failed!')
        except subprocess.TimeoutExpired:
            print('Sending keyboard interrupt')
            for _ in range(3):
                ctypes.windll.kernel32.GenerateConsoleCtrlEvent(1, proc.pid)
                time.sleep(1)
            try:
                proc.wait(10)
            except:
                proc.kill()
            raise KeyboardInterrupt


def _make_tmp_paths():
    """Creates TMP and TEMP variable dirs so ninja won't fail"""
    tmp_path = Path(os.environ['TMP'])
    if not tmp_path.exists():
        tmp_path.mkdir()
    tmp_path = Path(os.environ['TEMP'])
    if not tmp_path.exists():
        tmp_path.mkdir()


def main():
    """CLI Entrypoint"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        '--disable-ssl-verification',
        action='store_true',
        help='Disables SSL verification for downloading')
    parser.add_argument(
        '--7z-path',
        dest='sevenz_path',
        default=USE_REGISTRY,
        help=('Command or path to 7-Zip\'s "7z" binary. If "_use_registry" is '
              'specified, determine the path from the registry. Default: %(default)s'))
    parser.add_argument(
        '--winrar-path',
        dest='winrar_path',
        default=USE_REGISTRY,
        help=('Command or path to WinRAR\'s "winrar.exe" binary. If "_use_registry" is '
              'specified, determine the path from the registry. Default: %(default)s'))
    parser.add_argument(
        '-j',
        type=int,
        dest='thread_count',
        help=('Number of CPU threads to use for compiling'))
    parser.add_argument(
        '--ci',
        action='store_true'
    )
    parser.add_argument(
        '--x86',
        action='store_true'
    )
    parser.add_argument(
        '--arm',
        action='store_true'
    )
    parser.add_argument(
        '--tarball',
        action='store_true'
    )
    parser.add_argument(
        '--target',
        dest='targets',
        action='append',
        help=('Ninja target to build instead of the default release targets. '
              'May be specified more than once.'))
    parser.add_argument(
        '--ci-ninja-timeout',
        dest='ci_ninja_timeout',
        type=int,
        default=None,
        help='Ninja timeout in seconds for CI (provided by stage action)')
    args = parser.parse_args()

    # Set common variables
    source_tree = _ROOT_DIR / 'build' / 'src'
    downloads_cache = _ROOT_DIR / 'build' / 'download_cache'

    if not args.ci or not (source_tree / 'BUILD.gn').exists():
        # Setup environment
        source_tree.mkdir(parents=True, exist_ok=True)
        downloads_cache.mkdir(parents=True, exist_ok=True)
        _make_tmp_paths()

        # Extractors
        extractors = {
            ExtractorEnum.SEVENZIP: args.sevenz_path,
            ExtractorEnum.WINRAR: args.winrar_path,
        }

        # Prepare source folder
        if args.tarball:
            # Download chromium tarball
            get_logger().info('Downloading chromium tarball...')
            download_info = downloads.DownloadInfo([_ROOT_DIR / 'ungoogled-chromium' / 'downloads.ini'])
            downloads.retrieve_downloads(download_info, downloads_cache, None, True, args.disable_ssl_verification)
            try:
                downloads.check_downloads(download_info, downloads_cache, None)
            except downloads.HashMismatchError as exc:
                get_logger().error('File checksum does not match: %s', exc)
                exit(1)

            # Unpack chromium tarball
            get_logger().info('Unpacking chromium tarball...')
            downloads.unpack_downloads(download_info, downloads_cache, None, source_tree, extractors)
        else:
            # Clone sources (with retry for network resilience)
            _clone_args = [sys.executable, str(Path('ungoogled-chromium', 'utils', 'clone.py')), '-o', 'build\\src', '-p', 'win32' if args.x86 else 'win-arm64' if args.arm else 'win64']
            for attempt in range(3):
                get_logger().info('Cloning Chromium sources (attempt %d/3)...', attempt + 1)
                try:
                    subprocess.run(_clone_args, check=True)
                    break
                except subprocess.CalledProcessError as e:
                    if attempt < 2:
                        get_logger().warning('Clone attempt %d failed (ret=%d), retrying in 30s...', attempt + 1, e.returncode)
                        time.sleep(30)
                    else:
                        get_logger().error('All clone attempts failed.')
                        raise

        # Retrieve windows downloads (with retry for network resilience)
        get_logger().info('Downloading required files...')
        download_info_win = downloads.DownloadInfo([_ROOT_DIR / 'downloads.ini'])
        target_download_components = None
        if args.targets:
            # Target mode disables Rust in args.gn, so no Rust package is needed
            # to bootstrap GN or compile the selected C++ object. Avoid extracting
            # all three large Rust toolchains on disk-constrained CI runners.
            target_download_components = [
                name for name in download_info_win if not name.startswith('rust-')
            ]
        for attempt in range(3):
            try:
                downloads.retrieve_downloads(download_info_win, downloads_cache,
                                             target_download_components, True,
                                             args.disable_ssl_verification)
                break
            except subprocess.CalledProcessError as exc:
                if attempt < 2:
                    get_logger().warning('Download attempt %d failed (ret=%d), retrying in 30s...',
                                         attempt + 1, exc.returncode)
                    for partial_file in downloads_cache.glob('*.partial'):
                        partial_file.unlink()
                    time.sleep(30)
                else:
                    get_logger().error('All download attempts failed.')
                    raise
        try:
            downloads.check_downloads(download_info_win, downloads_cache,
                                      target_download_components)
        except downloads.HashMismatchError as exc:
            get_logger().error('File checksum does not match: %s', exc)
            exit(1)

        # Prune binaries
        pruning_list = (_ROOT_DIR / 'ungoogled-chromium' / 'pruning.list') if args.tarball else (_ROOT_DIR  / 'pruning.list')
        unremovable_files = prune_binaries.prune_files(
            source_tree,
            pruning_list.read_text(encoding=ENCODING).splitlines()
        )
        if unremovable_files:
            get_logger().error('Files could not be pruned: %s', unremovable_files)
            parser.exit(1)

        # Unpack downloads
        DIRECTX = source_tree / 'third_party' / 'microsoft_dxheaders' / 'src'
        ESBUILD = source_tree / 'third_party' / 'devtools-frontend' / 'src' / 'third_party' / 'esbuild'
        if DIRECTX.exists():
            shutil.rmtree(DIRECTX)
            DIRECTX.mkdir()
        if ESBUILD.exists():
            shutil.rmtree(ESBUILD)
            ESBUILD.mkdir()
        get_logger().info('Unpacking downloads...')
        downloads.unpack_downloads(download_info_win, downloads_cache,
                                   target_download_components, source_tree, extractors)

        # Apply patches
        # First, ungoogled-chromium-patches
        patches.apply_patches(
            patches.generate_patches_from_series(_ROOT_DIR / 'ungoogled-chromium' / 'patches', resolve=True),
            source_tree,
            patch_bin_path=(source_tree / _PATCH_BIN_RELPATH)
        )
        # Then Windows-specific patches
        patches.apply_patches(
            patches.generate_patches_from_series(_ROOT_DIR / 'patches', resolve=True),
            source_tree,
            patch_bin_path=(source_tree / _PATCH_BIN_RELPATH)
        )

        # Patch third_party/node/node.py to disable WASM tier-up and prevent WASM JIT crash on Windows
        node_py_path = source_tree / 'third_party' / 'node' / 'node.py'
        if node_py_path.exists():
            node_py_content = node_py_path.read_text(encoding=ENCODING)
            if 'cmd = [GetBinaryPath()] + cmd_parts' in node_py_content:
                node_py_content = node_py_content.replace(
                    'cmd = [GetBinaryPath()] + cmd_parts',
                    'cmd = [GetBinaryPath(), "--no-wasm-tier-up", "--no-wasm-code-gc", "--v8-pool-size=1"] + cmd_parts'
                )
                node_py_path.write_text(node_py_content, encoding=ENCODING)
                get_logger().info('Successfully patched third_party/node/node.py with V8 WASM flags')

        # Substitute domains
        domain_substitution_list = (_ROOT_DIR / 'ungoogled-chromium' / 'domain_substitution.list') if args.tarball else (_ROOT_DIR  / 'domain_substitution.list')
        domain_substitution.apply_substitution(
            _ROOT_DIR / 'ungoogled-chromium' / 'domain_regex.list',
            domain_substitution_list,
            source_tree,
            None
        )

    # Check if rust-toolchain folder has been populated
    HOST_CPU_IS_64BIT = sys.maxsize > 2**32
    RUST_DIR_DST = source_tree / 'third_party' / 'rust-toolchain'
    RUST_DIR_SRC64 = source_tree / 'third_party' / 'rust-toolchain-x64'
    RUST_DIR_SRC86 = source_tree / 'third_party' / 'rust-toolchain-x86'
    RUST_DIR_SRCARM = source_tree / 'third_party' / 'rust-toolchain-arm'
    RUST_FLAG_FILE = RUST_DIR_DST / 'INSTALLED_VERSION'
    if not args.targets and (not args.ci or not RUST_FLAG_FILE.exists()):
        # Directories to copy from source to target folder
        DIRS_TO_COPY = ['bin', 'lib']

        # A targeted x64 Ninja build does not need foreign-architecture Rust
        # standard libraries. Avoid copying them on disk-constrained CI runners.
        rust_source_dirs = [RUST_DIR_SRC64] if args.targets else [
            RUST_DIR_SRC64, RUST_DIR_SRC86, RUST_DIR_SRCARM
        ]
        # Loop over all required source folders.
        for rust_dir_src in rust_source_dirs:
            # Loop over all dirs to copy
            for dir_to_copy in DIRS_TO_COPY:
                # Copy bin folder for host architecture
                if (dir_to_copy == 'bin') and (HOST_CPU_IS_64BIT != (rust_dir_src == RUST_DIR_SRC64)):
                    continue

                # Create target dir
                target_dir = RUST_DIR_DST / dir_to_copy
                if not os.path.isdir(target_dir):
                    os.makedirs(target_dir)

                # Loop over all subfolders of the rust source dir
                for cp_src in rust_dir_src.glob(f'*/{dir_to_copy}/*'):
                    cp_dst = target_dir / cp_src.name
                    if cp_src.is_dir():
                        shutil.copytree(cp_src, cp_dst, dirs_exist_ok=True)
                    else:
                        shutil.copy2(cp_src, cp_dst)

        # Generate version file
        with open(RUST_FLAG_FILE, 'w') as f:
            subprocess.run([source_tree / 'third_party' / 'rust-toolchain-x64' / 'rustc' / 'bin' / 'rustc.exe', '--version'], stdout=f)

    if not args.ci or not (source_tree / 'out/Default').exists():
        # Output args.gn
        (source_tree / 'out/Default').mkdir(parents=True)
        gn_flags = (_ROOT_DIR / 'ungoogled-chromium' / 'flags.gn').read_text(encoding=ENCODING)
        gn_flags += '\n'
        windows_flags = (_ROOT_DIR / 'flags.windows.gn').read_text(encoding=ENCODING)
        if args.x86:
            windows_flags = windows_flags.replace('x64', 'x86')
        elif args.arm:
            windows_flags = windows_flags.replace('x64', 'arm64')
        if args.tarball or args.targets:
            windows_flags += '\nchrome_pgo_phase=0\n'
        if args.targets:
            # This path is a C++-only targeted compile validation. It avoids
            # downloading and merging the multi-architecture Rust toolchains.
            windows_flags += 'enable_rust=false\n'
            # Chromium 151 imports PDFium fuzzers from the root build graph;
            # its Rust PNG feature must be disabled together with Rust.
            windows_flags += 'pdf_enable_rust_png=false\n'
        gn_flags += windows_flags
        (source_tree / 'out/Default/args.gn').write_text(gn_flags, encoding=ENCODING)

    # Enter source tree to run build commands
    os.chdir(source_tree)

    if not args.ci or not os.path.exists('out\\Default\\gn.exe'):
        # Run GN bootstrap
        _run_build_process(
            sys.executable, 'tools\\gn\\bootstrap\\bootstrap.py', '-o', 'out\\Default\\gn.exe',
            '--skip-generate-buildfiles')

        # Run gn gen
        _run_build_process('out\\Default\\gn.exe', 'gen', 'out\\Default', '--fail-on-unused-args')

    if (not args.targets and
            (not args.ci or not os.path.exists('third_party\\rust-toolchain\\bin\\bindgen.exe'))):
        # Build bindgen
        _run_build_process(
            sys.executable,
            'tools\\rust\\build_bindgen.py', '--skip-test')

    # Ninja commandline
    ninja_commandline = ['third_party\\ninja\\ninja.exe']
    if args.thread_count is not None:
        ninja_commandline.append('-j')
        ninja_commandline.append(args.thread_count)
    ninja_commandline.append('-C')
    ninja_commandline.append('out\\Default')
    if args.targets:
        ninja_commandline.extend(args.targets)
    else:
        ninja_commandline.append('chrome')
        ninja_commandline.append('chromedriver')
        ninja_commandline.append('mini_installer')

    # Run ninja
    if args.ci:
        ninja_timeout = _ninja_timeout_seconds(args.ci_ninja_timeout)
        python_elapsed = time.monotonic() - _PROCESS_START_MONOTONIC
        if args.ci_ninja_timeout is not None:
            print(
                'CI stage: JS-provided ninja budget %ds (%.1fh), python_elapsed %.0fs'
                % (ninja_timeout, ninja_timeout / 3600.0, python_elapsed))
        else:
            print(
                'CI stage: elapsed %.0fs before ninja (fallback), ninja budget %.0fs (%.1fh)'
                % (python_elapsed, ninja_timeout, ninja_timeout / 3600.0))
        try:
            _run_build_process_timeout(*ninja_commandline, timeout=ninja_timeout)
        except KeyboardInterrupt:
            sys.exit(_CI_STAGE_TIMEOUT_EXIT_CODE)
        # Packaging only applies to the default release target set.
        if not args.targets:
            os.chdir(_ROOT_DIR)
            subprocess.run([sys.executable, 'package.py', '--cpu-arch', '32bit' if args.x86 else 'arm' if args.arm else '64bit'])
    else:
        _run_build_process(*ninja_commandline)


if __name__ == '__main__':
    main()
