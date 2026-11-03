namespace Morphir.Configuration

/// Pure normalization shared by typed in-memory hosts and format adapters.
module Normalization =
    let normalize source raw positions =
        { Source = source
          Raw = raw
          Values = raw
          Positions = positions
          Diagnostics = []
          LegacyFields = [] }
