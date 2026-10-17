"""ETABS COM/OAPI Connector Module.

Provides a robust Python interface for connecting to and controlling
CSI ETABS via the COM automation (OAPI) interface. Supports attaching
to a running instance or launching a new one, with full error handling,
logging, and context-manager support.

Example usage::

    with EtabsConnector() as etabs:
        etabs.connect(attach_existing=True)
        etabs.set_units()
        info = etabs.get_model_info()
        print(info)
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any, Dict, Optional

import comtypes.client

logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Custom exception
# ---------------------------------------------------------------------------

class EtabsConnectionError(Exception):
    """Raised when a connection or COM operation with ETABS fails."""


# ---------------------------------------------------------------------------
# Helper: locate ETABS executable
# ---------------------------------------------------------------------------

_ETABS_SEARCH_DIRS: list[str] = [
    r"C:\Program Files\Computers and Structures\ETABS 22",
    r"C:\Program Files\Computers and Structures\ETABS 21",
    r"C:\Program Files\Computers and Structures\ETABS 20",
]

_ETABS_EXE_NAME = "ETABS.exe"


def find_etabs_exe() -> str:
    """Search common installation directories for the ETABS executable.

    The function checks directories in descending version order (22 → 21 → 20)
    and returns the **first** path where ``ETABS.exe`` is found.

    Returns:
        str: Absolute path to the ETABS executable.

    Raises:
        FileNotFoundError: If no ETABS installation is found in any of the
            searched directories.
    """
    for directory in _ETABS_SEARCH_DIRS:
        candidate = os.path.join(directory, _ETABS_EXE_NAME)
        if os.path.isfile(candidate):
            logger.info("Found ETABS executable at: %s", candidate)
            return candidate
    searched = "\n  ".join(_ETABS_SEARCH_DIRS)
    raise FileNotFoundError(
        f"ETABS executable not found. Searched:\n  {searched}"
    )


# ---------------------------------------------------------------------------
# Main connector class
# ---------------------------------------------------------------------------

class EtabsConnector:
    """High-level wrapper around the CSI ETABS COM/OAPI interface.

    Parameters
    ----------
    None

    Attributes
    ----------
    etabs_object : Any
        The top-level ``ETABSObject`` COM instance.
    sap_model : Any
        The ``SapModel`` obtained from ``etabs_object``.
    """

    # ETABS COM ProgID
    _PROG_ID = "CSI.ETABS.API.ETABSObject"

    def __init__(self) -> None:
        self.etabs_object: Any = None
        self.sap_model: Any = None
        logger.debug("EtabsConnector instance created.")

    # ------------------------------------------------------------------
    # Context-manager protocol
    # ------------------------------------------------------------------

    def __enter__(self) -> "EtabsConnector":
        return self

    def __exit__(
        self,
        exc_type: Optional[type],
        exc_val: Optional[BaseException],
        exc_tb: Any,
    ) -> None:
        # We deliberately do NOT close ETABS on exit – the user may want it
        # to stay open.  We just release our COM references.
        self.etabs_object = None
        self.sap_model = None
        logger.debug("EtabsConnector context exited; COM references released.")

    # ------------------------------------------------------------------
    # Connection
    # ------------------------------------------------------------------

    def connect(self, attach_existing: bool = True) -> bool:
        """Connect to a running ETABS instance or launch a new one.

        Parameters
        ----------
        attach_existing : bool, optional
            If ``True`` (default), attempt to attach to an already-running
            ETABS process first.  If ``False`` **and** no running instance
            is found, a new ETABS process will be started.

        Returns
        -------
        bool
            ``True`` if the connection was established successfully.

        Raises
        ------
        EtabsConnectionError
            If the connection could not be established.
        """
        # --- Step 1: try attaching to an active instance -----------------
        try:
            self.etabs_object = comtypes.client.GetActiveObject(self._PROG_ID)
            logger.info("Attached to running ETABS instance.")
        except (OSError, comtypes.COMError):
            logger.debug("No active ETABS instance found.")
            if attach_existing:
                raise EtabsConnectionError(
                    "No running ETABS instance found and attach_existing=True. "
                    "Set attach_existing=False to launch a new instance."
                )

            # --- Step 2: launch a new instance ---------------------------
            try:
                etabs_exe = find_etabs_exe()
                logger.info("Starting new ETABS instance from: %s", etabs_exe)

                helper = comtypes.client.CreateObject("ETABSv1.Helper")
                helper = helper.QueryInterface(comtypes.gen.ETABSv1.cHelper)
                self.etabs_object = helper.CreateObject(etabs_exe)

                logger.info("New ETABS instance created successfully.")
            except (OSError, comtypes.COMError, FileNotFoundError) as exc:
                raise EtabsConnectionError(
                    f"Failed to launch a new ETABS instance: {exc}"
                ) from exc

        # --- Step 3: obtain SapModel ------------------------------------
        try:
            self.sap_model = self.etabs_object.SapModel
            logger.info("SapModel interface acquired.")
        except (AttributeError, comtypes.COMError) as exc:
            raise EtabsConnectionError(
                f"Failed to obtain SapModel from ETABS object: {exc}"
            ) from exc

        return True

    # ------------------------------------------------------------------
    # Units
    # ------------------------------------------------------------------

    def set_units(
        self,
        force: int = 6,
        length: int = 5,
        temp: int = 3,
    ) -> None:
        """Set the present display units.

        The default arguments correspond to **kN · m · °C**:

        * ``force``  – ``eForce_kN``  = 6
        * ``length`` – ``eLength_m``  = 5
        * ``temp``   – ``eTemp_C``    = 3

        Internally calls ``SapModel.SetPresentUnits_2(force, length, temp, 2, 1)``
        where the fourth argument (2) selects **Degrees** as the angular unit
        and the fifth (1) selects **Seconds** as the time unit.

        Raises
        ------
        EtabsConnectionError
            If the COM call fails or returns a non-zero code.
        """
        self._ensure_connected()
        try:
            ret = self.sap_model.SetPresentUnits_2(force, length, temp, 2, 1)
            if ret != 0:
                raise EtabsConnectionError(
                    f"SetPresentUnits_2 returned error code {ret}."
                )
            logger.info(
                "Units set: force=%d, length=%d, temp=%d.", force, length, temp
            )
        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error while setting units: {exc}"
            ) from exc

    # ------------------------------------------------------------------
    # Model operations
    # ------------------------------------------------------------------

    def unlock_model(self) -> None:
        """Unlock the model if it is currently locked.

        Raises
        ------
        EtabsConnectionError
            If the COM call fails.
        """
        self._ensure_connected()
        try:
            ret = self.sap_model.SetModelIsLocked(False)
            if ret != 0:
                raise EtabsConnectionError(
                    f"SetModelIsLocked(False) returned error code {ret}."
                )
            logger.info("Model unlocked.")
        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error while unlocking model: {exc}"
            ) from exc

    def new_model(
        self,
        num_stories: int = 1,
        story_height: float = 3.0,
    ) -> None:
        """Create a new blank ETABS model.

        Parameters
        ----------
        num_stories : int, optional
            Number of stories (default 1).  *Reserved for future template
            use – the current implementation creates a blank model.*
        story_height : float, optional
            Typical story height in current length units (default 3.0 m).
            *Reserved for future template use.*

        Raises
        ------
        EtabsConnectionError
            If model initialisation fails.
        """
        self._ensure_connected()
        try:
            # eUnits_kN_m_C = 6 for InitializeNewModel
            ret = self.sap_model.InitializeNewModel(6)
            if ret != 0:
                raise EtabsConnectionError(
                    f"InitializeNewModel returned error code {ret}."
                )
            logger.info("New model initialised (units preset = kN_m_C).")

            ret = self.sap_model.File.NewBlank()
            if ret != 0:
                raise EtabsConnectionError(
                    f"File.NewBlank returned error code {ret}."
                )
            logger.info(
                "Blank model created (num_stories=%d, story_height=%.2f — "
                "template parameters reserved for future use).",
                num_stories,
                story_height,
            )
        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error while creating new model: {exc}"
            ) from exc

    def save_model(self, path: str) -> None:
        """Save the current model to *path*.

        Parameters
        ----------
        path : str
            Full file path (e.g. ``r"C:\\Models\\MyModel.EDB"``).

        Raises
        ------
        EtabsConnectionError
            If the save operation fails.
        """
        self._ensure_connected()
        try:
            # Ensure the target directory exists
            parent = Path(path).parent
            parent.mkdir(parents=True, exist_ok=True)

            ret = self.sap_model.File.Save(path)
            if ret != 0:
                raise EtabsConnectionError(
                    f"File.Save returned error code {ret}."
                )
            logger.info("Model saved to: %s", path)
        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error while saving model: {exc}"
            ) from exc

    # ------------------------------------------------------------------
    # Status / information
    # ------------------------------------------------------------------

    def is_connected(self) -> bool:
        """Return ``True`` if both ``etabs_object`` and ``sap_model`` are set.

        This performs a lightweight attribute check; it does **not** ping
        the COM server.
        """
        return self.etabs_object is not None and self.sap_model is not None

    def get_model_info(self) -> Dict[str, Any]:
        """Retrieve summary information about the current model.

        Returns
        -------
        dict
            A dictionary with keys:

            * ``filename`` – current model file path
            * ``units`` – integer code for present units
            * ``num_frames`` – number of frame objects
            * ``num_areas`` – number of area objects
            * ``num_joints`` – number of joint (point) objects
            * ``num_load_patterns`` – number of defined load patterns

        Raises
        ------
        EtabsConnectionError
            If any COM call fails.
        """
        self._ensure_connected()
        try:
            filename: str = self.sap_model.GetModelFilename()
            units: int = self.sap_model.GetPresentUnits()

            # Object counts via CountObj (object type enum):
            #   eObjectType_Frame = 2
            #   eObjectType_Area  = 3
            #   eObjectType_Point = 1
            num_frames: int = self.sap_model.FrameObj.Count()
            num_areas: int = self.sap_model.AreaObj.Count()
            num_joints: int = self.sap_model.PointObj.Count()
            num_load_patterns: int = self.sap_model.LoadPatterns.Count()

            info: Dict[str, Any] = {
                "filename": filename,
                "units": units,
                "num_frames": num_frames,
                "num_areas": num_areas,
                "num_joints": num_joints,
                "num_load_patterns": num_load_patterns,
            }
            logger.info("Model info retrieved: %s", info)
            return info

        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error while retrieving model info: {exc}"
            ) from exc

    # ------------------------------------------------------------------
    # Analysis & design
    # ------------------------------------------------------------------

    def run_analysis(self) -> bool:
        """Lock the model and run the analysis.

        Returns
        -------
        bool
            ``True`` if the analysis completed without errors.

        Raises
        ------
        EtabsConnectionError
            If the analysis fails.
        """
        self._ensure_connected()
        try:
            # Lock the model before running analysis
            ret = self.sap_model.SetModelIsLocked(True)
            if ret != 0:
                raise EtabsConnectionError(
                    f"SetModelIsLocked(True) returned error code {ret}."
                )
            logger.info("Model locked for analysis.")

            ret = self.sap_model.Analyze.RunAnalysis()
            if ret != 0:
                raise EtabsConnectionError(
                    f"Analyze.RunAnalysis returned error code {ret}."
                )
            logger.info("Analysis completed successfully.")
            return True

        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error during analysis: {exc}"
            ) from exc

    def start_design(self) -> bool:
        """Run the concrete frame design.

        Returns
        -------
        bool
            ``True`` if the design completed without errors.

        Raises
        ------
        EtabsConnectionError
            If the design process fails.
        """
        self._ensure_connected()
        try:
            ret = self.sap_model.DesignConcrete.StartDesign()
            if ret != 0:
                raise EtabsConnectionError(
                    f"DesignConcrete.StartDesign returned error code {ret}."
                )
            logger.info("Concrete frame design completed successfully.")
            return True

        except comtypes.COMError as exc:
            raise EtabsConnectionError(
                f"COM error during concrete design: {exc}"
            ) from exc

    # ------------------------------------------------------------------
    # Advanced access
    # ------------------------------------------------------------------

    def get_sap_model(self) -> Any:
        """Return the raw ``SapModel`` COM object for advanced OAPI use.

        Returns
        -------
        Any
            The ``SapModel`` COM interface. ``None`` if not connected.
        """
        return self.sap_model

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _ensure_connected(self) -> None:
        """Raise if we are not connected to ETABS."""
        if not self.is_connected():
            raise EtabsConnectionError(
                "Not connected to ETABS. Call connect() first."
            )
